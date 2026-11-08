import 'dart:convert';
import 'package:mindlift_api/mindlift_api.dart';

class LocalCachedRecord {
  const LocalCachedRecord({
    required this.id,
    required this.entityType,
    required this.title,
    this.subtitle,
    required this.status,
    required this.payloadJson,
    required this.updatedAtUtc,
  });

  final String id;
  final String entityType;
  final String title;
  final String? subtitle;
  final String status;
  final String payloadJson;
  final DateTime updatedAtUtc;

  Map<String, dynamic> get payloadMap {
    try {
      return jsonDecode(payloadJson) as Map<String, dynamic>;
    } catch (_) {
      return {};
    }
  }
}

class LocalOutboxMutation {
  const LocalOutboxMutation({
    required this.clientMutationId,
    required this.entityType,
    required this.operation,
    required this.clientTempId,
    required this.payloadJson,
    this.status = 'pending', // pending, syncing, applied, failed, conflict
    this.retryCount = 0,
    required this.createdAtUtc,
  });

  final String clientMutationId;
  final String entityType;
  final String operation;
  final String clientTempId;
  final String payloadJson;
  final String status;
  final int retryCount;
  final DateTime createdAtUtc;

  Map<String, dynamic> get payloadMap {
    try {
      return jsonDecode(payloadJson) as Map<String, dynamic>;
    } catch (_) {
      return {};
    }
  }

  OfflineClientMutationDto toDto() {
    return OfflineClientMutationDto(
      clientMutationId: clientMutationId,
      entityType: entityType,
      operation: operation,
      clientTempId: clientTempId,
      payload: payloadMap,
      occurredAtUtc: createdAtUtc,
    );
  }
}

/// In-memory and SQLCipher compatible offline database interface.
class OfflineDatabase {
  final Map<String, LocalCachedRecord> _records = {};
  final List<LocalOutboxMutation> _outbox = [];

  Future<void> cacheRecords(List<LocalCachedRecord> records) async {
    for (final r in records) {
      _records['${r.entityType}_${r.id}'] = r;
    }
  }

  Future<List<LocalCachedRecord>> getCachedRecords(String entityType) async {
    return _records.values.where((r) => r.entityType == entityType).toList();
  }

  Future<LocalCachedRecord?> getCachedRecord(String entityType, String id) async {
    return _records['${entityType}_$id'];
  }

  Future<void> queueMutation(LocalOutboxMutation mutation) async {
    _outbox.add(mutation);
  }

  Future<List<LocalOutboxMutation>> getPendingMutations() async {
    return _outbox.where((m) => m.status == 'pending' || m.status == 'failed').toList();
  }

  Future<int> getOutboxCount() async {
    return _outbox.where((m) => m.status != 'applied').length;
  }

  Future<void> markMutationStatus(String clientMutationId, String status) async {
    final idx = _outbox.indexWhere((m) => m.clientMutationId == clientMutationId);
    if (idx != -1) {
      final existing = _outbox[idx];
      _outbox[idx] = LocalOutboxMutation(
        clientMutationId: existing.clientMutationId,
        entityType: existing.entityType,
        operation: existing.operation,
        clientTempId: existing.clientTempId,
        payloadJson: existing.payloadJson,
        status: status,
        retryCount: status == 'failed' ? existing.retryCount + 1 : existing.retryCount,
        createdAtUtc: existing.createdAtUtc,
      );
    }
  }

  Future<void> purgeDatabase() async {
    _records.clear();
    _outbox.clear();
  }
}

