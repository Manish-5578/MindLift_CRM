import 'lead_models.dart';
import 'deal_models.dart';
import 'contact_models.dart';
import 'activity_models.dart';

class OfflineBootstrapDto {
  const OfflineBootstrapDto({
    this.leads = const [],
    this.deals = const [],
    this.contacts = const [],
    this.activities = const [],
    this.syncCursor,
  });

  final List<LeadDto> leads;
  final List<DealDto> deals;
  final List<ContactDto> contacts;
  final List<ActivityDto> activities;
  final String? syncCursor;

  factory OfflineBootstrapDto.fromJson(Map<String, dynamic> json) {
    return OfflineBootstrapDto(
      leads: (json['leads'] as List<dynamic>?)
              ?.map((e) => LeadDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      deals: (json['deals'] as List<dynamic>?)
              ?.map((e) => DealDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      contacts: (json['contacts'] as List<dynamic>?)
              ?.map((e) => ContactDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      activities: (json['activities'] as List<dynamic>?)
              ?.map((e) => ActivityDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      syncCursor: json['syncCursor'] as String? ?? json['serverSyncCursor'] as String?,
    );
  }
}

class OfflineClientMutationDto {
  const OfflineClientMutationDto({
    required this.clientMutationId,
    required this.entityType,
    required this.operation,
    required this.clientTempId,
    required this.payload,
    this.baseVersion = 1,
    this.occurredAtUtc,
  });

  final String clientMutationId;
  final String entityType; // Lead, Activity, Deal, Note
  final String operation; // Create, Update, Complete
  final String clientTempId;
  final Map<String, dynamic> payload;
  final int baseVersion;
  final DateTime? occurredAtUtc;

  Map<String, dynamic> toJson() => {
        'clientMutationId': clientMutationId,
        'entityType': entityType,
        'operation': operation,
        'clientTempId': clientTempId,
        'payload': payload,
        'baseVersion': baseVersion,
        'occurredAtUtc': (occurredAtUtc ?? DateTime.now().toUtc()).toIso8601String(),
      };
}

class OfflineBatchSyncRequest {
  const OfflineBatchSyncRequest({
    required this.batchId,
    this.syncCursor,
    required this.mutations,
  });

  final String batchId;
  final String? syncCursor;
  final List<OfflineClientMutationDto> mutations;

  Map<String, dynamic> toJson() => {
        'batchId': batchId,
        if (syncCursor != null) 'syncCursor': syncCursor,
        'mutations': mutations.map((m) => m.toJson()).toList(),
      };
}

class OfflineMutationResultDto {
  const OfflineMutationResultDto({
    required this.clientMutationId,
    required this.status, // "Applied", "Conflict", "Error"
    this.serverEntityId,
    this.newVersion,
    this.message,
    this.serverRecordJson,
  });

  final String clientMutationId;
  final String status;
  final String? serverEntityId;
  final int? newVersion;
  final String? message;
  final String? serverRecordJson;

  factory OfflineMutationResultDto.fromJson(Map<String, dynamic> json) {
    return OfflineMutationResultDto(
      clientMutationId: json['clientMutationId'] as String? ?? '',
      status: json['status'] as String? ?? 'Applied',
      serverEntityId: json['serverEntityId'] as String?,
      newVersion: (json['newVersion'] as num?)?.toInt(),
      message: json['message'] as String?,
      serverRecordJson: json['serverRecordJson'] as String?,
    );
  }
}

class OfflineBatchSyncResultDto {
  const OfflineBatchSyncResultDto({
    required this.batchId,
    this.results = const [],
    this.newSyncCursor,
  });

  final String batchId;
  final List<OfflineMutationResultDto> results;
  final String? newSyncCursor;

  factory OfflineBatchSyncResultDto.fromJson(Map<String, dynamic> json) {
    return OfflineBatchSyncResultDto(
      batchId: json['batchId'] as String? ?? '',
      results: (json['results'] as List<dynamic>?)
              ?.map((e) => OfflineMutationResultDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      newSyncCursor: json['newSyncCursor'] as String?,
    );
  }
}
