import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:dio/dio.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'network_provider.dart';

/// True when a request failed because the phone could not reach the server (as opposed to the server saying no).
bool isOfflineError(Object e) {
  if (e is! DioException || e.response != null) return false;
  return e.type == DioExceptionType.connectionError ||
      e.type == DioExceptionType.connectionTimeout ||
      e.type == DioExceptionType.sendTimeout ||
      e.type == DioExceptionType.receiveTimeout ||
      e.type == DioExceptionType.unknown;
}

class OutboxItem {
  OutboxItem({
    required this.id,
    required this.method,
    required this.path,
    required this.body,
    required this.label,
    required this.createdAt,
    this.error,
  });

  final String id;
  final String method;
  final String path;
  final Map<String, dynamic>? body;

  /// What the person did, in words ("Create lead Acme").
  final String label;
  final DateTime createdAt;

  /// Set when the server rejected the change, so it will not be retried automatically.
  String? error;

  Map<String, dynamic> toJson() => {
        'id': id,
        'method': method,
        'path': path,
        'body': body,
        'label': label,
        'createdAt': createdAt.toIso8601String(),
        'error': error,
      };

  factory OutboxItem.fromJson(Map<String, dynamic> j) => OutboxItem(
        id: j['id'] as String,
        method: j['method'] as String,
        path: j['path'] as String,
        body: j['body'] is Map ? Map<String, dynamic>.from(j['body'] as Map) : null,
        label: j['label'] as String? ?? '',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
        error: j['error'] as String?,
      );
}

/// Saved copies of what the person last saw, plus the changes made while offline, kept in a private file on the phone
/// and wiped on sign-out.
class OfflineStore {
  OfflineStore({Future<File> Function()? fileProvider}) : _fileProvider = fileProvider;

  final Future<File> Function()? _fileProvider;

  Map<String, dynamic> _cache = {};
  List<OutboxItem> _outbox = [];
  File? _file;
  Timer? _saveTimer;

  Future<void>? _loading;
  bool _loaded = false;

  /// Loads the file once, even when several callers ask at the same time.
  Future<void> _ensure() => _loading ??= _load();

  Future<void> _load() async {
    try {
      if (_fileProvider != null) {
        _file = await _fileProvider!();
      } else {
        final dir = await getApplicationSupportDirectory();
        _file = File('${dir.path}/offline_store.json');
      }
      if (await _file!.exists()) {
        final j = jsonDecode(await _file!.readAsString()) as Map<String, dynamic>;
        _cache = Map<String, dynamic>.from(j['cache'] as Map? ?? {});
        _outbox = ((j['outbox'] as List?) ?? const []).whereType<Map>().map((e) => OutboxItem.fromJson(Map<String, dynamic>.from(e))).toList();
      }
    } catch (_) {
      _cache = {};
      _outbox = [];
    }
    _loaded = true;
  }

  void _scheduleSave() {
    _saveTimer?.cancel();
    _saveTimer = Timer(const Duration(milliseconds: 400), flushNow);
  }

  /// Writes to disk right away (also used when the app goes to the background, so nothing waits in memory).
  Future<void> flushNow() async {
    _saveTimer?.cancel();
    if (!_loaded) return;
    try {
      await _file?.writeAsString(jsonEncode({'cache': _cache, 'outbox': _outbox.map((e) => e.toJson()).toList()}), flush: true);
    } catch (_) {}
  }

  Future<void> putCache(String key, Object value) async {
    await _ensure();
    _cache[key] = {'at': DateTime.now().toIso8601String(), 'data': value};
    _scheduleSave();
  }

  Future<({Object data, DateTime at})?> getCache(String key) async {
    await _ensure();
    final e = _cache[key];
    if (e is! Map || e['data'] == null) return null;
    return (data: e['data'] as Object, at: DateTime.tryParse(e['at'] as String? ?? '') ?? DateTime.now());
  }

  Future<List<OutboxItem>> outbox() async {
    await _ensure();
    return List.unmodifiable(_outbox);
  }

  Future<void> add(OutboxItem item) async {
    await _ensure();
    _outbox.add(item);
    _scheduleSave();
  }

  Future<void> remove(String id) async {
    await _ensure();
    _outbox.removeWhere((e) => e.id == id);
    _scheduleSave();
  }

  Future<void> save() async {
    await _ensure();
    _scheduleSave();
  }

  Future<void> clear() async {
    _cache = {};
    _outbox = [];
    _scheduleSave();
  }
}

final offlineStoreProvider = Provider<OfflineStore>((ref) => OfflineStore());

class OutboxState {
  const OutboxState({this.online = true, this.syncing = false, this.pending = 0, this.failed = const [], this.lastSyncedAt});
  final bool online;
  final bool syncing;
  final int pending;
  final List<OutboxItem> failed;
  final DateTime? lastSyncedAt;

  OutboxState copyWith({bool? online, bool? syncing, int? pending, List<OutboxItem>? failed, DateTime? lastSyncedAt}) => OutboxState(
        online: online ?? this.online,
        syncing: syncing ?? this.syncing,
        pending: pending ?? this.pending,
        failed: failed ?? this.failed,
        lastSyncedAt: lastSyncedAt ?? this.lastSyncedAt,
      );
}

/// Watches the connection, keeps the queue of offline changes, and replays it in order when the phone is back online.
class OutboxNotifier extends StateNotifier<OutboxState> with WidgetsBindingObserver {
  OutboxNotifier(this._ref) : super(const OutboxState()) {
    WidgetsBinding.instance.addObserver(this);
    try {
      _sub = Connectivity().onConnectivityChanged.listen((r) {
        final on = r != ConnectivityResult.none;
        if (on != state.online) {
          state = state.copyWith(online: on);
          if (on) flush();
        }
      }, onError: (_) {});
      Connectivity().checkConnectivity().then((r) => state = state.copyWith(online: r != ConnectivityResult.none)).catchError((_) => state);
    } catch (_) {}
    refresh();
  }

  final Ref _ref;
  StreamSubscription<ConnectivityResult>? _sub;
  bool _flushing = false;

  OfflineStore get _store => _ref.read(offlineStoreProvider);
  Dio get _dio => _ref.read(apiClientProvider).rawDio;

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    if (s == AppLifecycleState.resumed) flush();
    if (s == AppLifecycleState.paused) _store.flushNow();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _sub?.cancel();
    super.dispose();
  }

  int _refreshGen = 0;
  static int _seq = 0; // two changes in the same microsecond must still get different ids

  Future<void> refresh() async {
    // Only the newest refresh may apply, so a slow earlier one can never overwrite fresher numbers.
    final gen = ++_refreshGen;
    final items = await _store.outbox();
    if (gen != _refreshGen || !mounted) return;
    state = state.copyWith(pending: items.where((e) => e.error == null).length, failed: items.where((e) => e.error != null).toList());
  }

  /// Marks the phone offline (a request just failed to connect) so every screen shows the banner straight away.
  void markOffline() {
    if (state.online) state = state.copyWith(online: false);
  }

  Future<void> enqueue(String method, String path, Map<String, dynamic>? body, String label) async {
    await _store.add(OutboxItem(id: '${DateTime.now().microsecondsSinceEpoch}-${_seq++}', method: method, path: path, body: body, label: label, createdAt: DateTime.now()));
    markOffline();
    await refresh();
  }

  Future<void> discard(String id) async {
    await _store.remove(id);
    await refresh();
  }

  Future<void> retry(String id) async {
    final items = await _store.outbox();
    for (final i in items.where((e) => e.id == id)) {
      i.error = null;
    }
    await _store.save();
    await refresh();
    flush();
  }

  /// Sends the waiting changes oldest first. Stops at the first connection failure; a change the server rejects is set aside
  /// (shown in Profile) so it never blocks the ones behind it.
  Future<void> flush() async {
    if (_flushing) return;
    _flushing = true;
    try {
      final items = (await _store.outbox()).where((e) => e.error == null).toList();
      if (items.isEmpty) {
        state = state.copyWith(online: true, syncing: false);
        return;
      }
      state = state.copyWith(syncing: true);
      for (final item in items) {
        try {
          await _dio.request<dynamic>(item.path, data: item.body, options: Options(method: item.method));
          await _store.remove(item.id);
        } on DioException catch (e) {
          if (isOfflineError(e)) {
            state = state.copyWith(online: false, syncing: false);
            await refresh();
            return;
          }
          final d = e.response?.data;
          item.error = (d is Map ? (d['detail'] ?? d['title']) as String? : null) ?? 'The server rejected this change.';
          await _store.save();
        }
      }
      state = state.copyWith(online: true, lastSyncedAt: DateTime.now());
    } finally {
      _flushing = false;
      state = state.copyWith(syncing: false);
      await refresh();
    }
  }
}

final outboxProvider = StateNotifierProvider<OutboxNotifier, OutboxState>((ref) => OutboxNotifier(ref));

/// Runs [send]; if the phone is offline, queues the same change to be sent later and returns false.
Future<bool> sendOrQueue(
  WidgetRef ref, {
  required Future<void> Function() send,
  required String method,
  required String path,
  Map<String, dynamic>? body,
  required String label,
}) async {
  try {
    await send();
    return true;
  } catch (e) {
    if (!isOfflineError(e)) rethrow;
    await ref.read(outboxProvider.notifier).enqueue(method, path, body, label);
    return false;
  }
}
