import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../features/offline/offline_database.dart';
import 'auth_provider.dart';

// Configurable base URL for staff API
const kDefaultApiBaseUrl = 'https://crm.mindlift.com';

final offlineDatabaseProvider = Provider<OfflineDatabase>((ref) {
  return OfflineDatabase();
});

// A session event notifier to decouple apiClientProvider from authNotifierProvider
class SessionEventNotifier extends StateNotifier<int> {
  SessionEventNotifier() : super(0);
  void triggerExpired() => state++;
}

final sessionEventProvider = StateNotifierProvider<SessionEventNotifier, int>((ref) {
  return SessionEventNotifier();
});

final apiClientProvider = Provider<MindLiftApiClient>((ref) {
  final tokenStorage = ref.watch(tokenStorageProvider);
  return MindLiftApiClient(
    baseUrl: kDefaultApiBaseUrl,
    tokenStorage: tokenStorage,
    onSessionExpired: () {
      ref.read(sessionEventProvider.notifier).triggerExpired();
    },
  );
});

final authServiceProvider = Provider<AuthService>((ref) {
  return AuthService(ref.watch(apiClientProvider));
});

final leadsServiceProvider = Provider<LeadsService>((ref) {
  return LeadsService(ref.watch(apiClientProvider));
});

final activitiesServiceProvider = Provider<ActivitiesService>((ref) {
  return ActivitiesService(ref.watch(apiClientProvider));
});

final dealsServiceProvider = Provider<DealsService>((ref) {
  return DealsService(ref.watch(apiClientProvider));
});

final notificationsServiceProvider = Provider<NotificationsService>((ref) {
  return NotificationsService(ref.watch(apiClientProvider));
});

final offlineSyncServiceProvider = Provider<OfflineSyncService>((ref) {
  return OfflineSyncService(ref.watch(apiClientProvider));
});

final authNotifierProvider = StateNotifierProvider<AuthNotifier, AuthState>((ref) {
  final tokenStorage = ref.watch(tokenStorageProvider);
  final authService = ref.watch(authServiceProvider);
  final db = ref.watch(offlineDatabaseProvider);

  final notifier = AuthNotifier(
    tokenStorage: tokenStorage,
    authService: authService,
    onPurgeLocalData: () async {
      await db.purgeDatabase();
    },
  );

  ref.listen<int>(sessionEventProvider, (_, __) {
    notifier.handleSessionExpired();
  });

  return notifier;
});

class SyncState {
  const SyncState({
    this.connectionState = MlConnectionState.online,
    this.pendingCount = 0,
    this.lastSyncedAt,
    this.errorMessage,
  });

  final MlConnectionState connectionState;
  final int pendingCount;
  final DateTime? lastSyncedAt;
  final String? errorMessage;

  SyncState copyWith({
    MlConnectionState? connectionState,
    int? pendingCount,
    DateTime? lastSyncedAt,
    String? errorMessage,
  }) {
    return SyncState(
      connectionState: connectionState ?? this.connectionState,
      pendingCount: pendingCount ?? this.pendingCount,
      lastSyncedAt: lastSyncedAt ?? this.lastSyncedAt,
      errorMessage: errorMessage,
    );
  }
}

class SyncCoordinator extends StateNotifier<SyncState> {
  SyncCoordinator({
    required this.offlineDb,
    required this.syncService,
  }) : super(const SyncState());

  final OfflineDatabase offlineDb;
  final OfflineSyncService syncService;

  Future<void> syncPendingMutations() async {
    final pending = await offlineDb.getPendingMutations();
    if (pending.isEmpty) {
      state = state.copyWith(
        connectionState: MlConnectionState.online,
        pendingCount: 0,
      );
      return;
    }

    state = state.copyWith(
      connectionState: MlConnectionState.syncing,
      pendingCount: pending.length,
    );

    try {
      final request = OfflineBatchSyncRequest(
        batchId: DateTime.now().millisecondsSinceEpoch.toString(),
        mutations: pending.map((m) => m.toDto()).toList(),
      );

      final result = await syncService.submitBatchSync(request);

      for (final res in result.results) {
        if (res.status == 'Applied') {
          await offlineDb.markMutationStatus(res.clientMutationId, 'applied');
        } else if (res.status == 'Conflict') {
          await offlineDb.markMutationStatus(res.clientMutationId, 'conflict');
        } else {
          await offlineDb.markMutationStatus(res.clientMutationId, 'failed');
        }
      }

      final remaining = await offlineDb.getOutboxCount();
      state = state.copyWith(
        connectionState: remaining > 0 ? MlConnectionState.conflict : MlConnectionState.online,
        pendingCount: remaining,
        lastSyncedAt: DateTime.now(),
      );
    } catch (e) {
      state = state.copyWith(
        connectionState: MlConnectionState.offline,
        errorMessage: 'Unable to reach sync server.',
      );
    }
  }

  void setOffline() {
    state = state.copyWith(connectionState: MlConnectionState.offline);
  }

  void setOnline() {
    state = state.copyWith(connectionState: MlConnectionState.online);
  }
}

final syncCoordinatorProvider = StateNotifierProvider<SyncCoordinator, SyncState>((ref) {
  return SyncCoordinator(
    offlineDb: ref.watch(offlineDatabaseProvider),
    syncService: ref.watch(offlineSyncServiceProvider),
  );
});
