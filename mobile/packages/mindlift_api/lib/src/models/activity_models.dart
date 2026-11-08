class ActivityDto {
  const ActivityDto({
    required this.id,
    required this.type,
    required this.subject,
    this.description,
    this.dueAtUtc,
    this.status = 'Open',
    this.priority = 'Medium',
    this.leadId,
    this.dealId,
    this.contactId,
    this.outcome,
    this.durationMinutes,
    this.assignedToUserId,
    this.assignedToName,
    this.createdAtUtc,
  });

  final String id;
  final String type; // Task, Call, Meeting, Note
  final String subject;
  final String? description;
  final DateTime? dueAtUtc;
  final String status; // Open, Completed, Cancelled
  final String priority; // High, Medium, Low
  final String? leadId;
  final String? dealId;
  final String? contactId;
  final String? outcome;
  final int? durationMinutes;
  final String? assignedToUserId;
  final String? assignedToName;
  final DateTime? createdAtUtc;

  factory ActivityDto.fromJson(Map<String, dynamic> json) {
    return ActivityDto(
      id: json['id'] as String? ?? '',
      type: json['type'] as String? ?? 'Task',
      subject: json['subject'] as String? ?? '',
      description: json['description'] as String?,
      dueAtUtc: json['dueAtUtc'] != null ? DateTime.tryParse(json['dueAtUtc'] as String) : null,
      status: json['status'] as String? ?? 'Open',
      priority: json['priority'] as String? ?? 'Medium',
      leadId: json['leadId'] as String?,
      dealId: json['dealId'] as String?,
      contactId: json['contactId'] as String?,
      outcome: json['outcome'] as String?,
      durationMinutes: (json['durationMinutes'] as num?)?.toInt(),
      assignedToUserId: json['assignedToUserId'] as String?,
      assignedToName: json['assignedToName'] as String?,
      createdAtUtc: json['createdAtUtc'] != null ? DateTime.tryParse(json['createdAtUtc'] as String) : null,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'type': type,
        'subject': subject,
        if (description != null) 'description': description,
        if (dueAtUtc != null) 'dueAtUtc': dueAtUtc!.toIso8601String(),
        'status': status,
        'priority': priority,
        if (leadId != null) 'leadId': leadId,
        if (dealId != null) 'dealId': dealId,
        if (contactId != null) 'contactId': contactId,
        if (outcome != null) 'outcome': outcome,
        if (durationMinutes != null) 'durationMinutes': durationMinutes,
        if (assignedToUserId != null) 'assignedToUserId': assignedToUserId,
        if (assignedToName != null) 'assignedToName': assignedToName,
        if (createdAtUtc != null) 'createdAtUtc': createdAtUtc!.toIso8601String(),
      };
}

class CreateActivityRequest {
  const CreateActivityRequest({
    required this.type,
    required this.subject,
    this.description,
    this.dueAtUtc,
    this.priority = 'Medium',
    this.leadId,
    this.dealId,
    this.contactId,
    this.assignedToUserId,
    this.durationMinutes,
  });

  final String type;
  final String subject;
  final String? description;
  final DateTime? dueAtUtc;
  final String priority;
  final String? leadId;
  final String? dealId;
  final String? contactId;
  final String? assignedToUserId;
  final int? durationMinutes;

  Map<String, dynamic> toJson() => {
        'type': type,
        'subject': subject,
        if (description != null) 'description': description,
        if (dueAtUtc != null) 'dueAtUtc': dueAtUtc!.toIso8601String(),
        'priority': priority,
        if (leadId != null) 'leadId': leadId,
        if (dealId != null) 'dealId': dealId,
        if (contactId != null) 'contactId': contactId,
        if (assignedToUserId != null) 'assignedToUserId': assignedToUserId,
        if (durationMinutes != null) 'durationMinutes': durationMinutes,
      };
}

class CompleteActivityRequest {
  const CompleteActivityRequest({
    this.outcome,
    this.note,
    this.durationMinutes,
  });

  final String? outcome;
  final String? note;
  final int? durationMinutes;

  Map<String, dynamic> toJson() => {
        if (outcome != null) 'outcome': outcome,
        if (note != null) 'note': note,
        if (durationMinutes != null) 'durationMinutes': durationMinutes,
      };
}

class RescheduleActivityRequest {
  const RescheduleActivityRequest({
    required this.newDueAtUtc,
    this.reason,
  });

  final DateTime newDueAtUtc;
  final String? reason;

  Map<String, dynamic> toJson() => {
        'newDueAtUtc': newDueAtUtc.toIso8601String(),
        if (reason != null) 'reason': reason,
      };
}

class MyDaySummaryDto {
  const MyDaySummaryDto({
    this.overdueCount = 0,
    this.dueTodayCount = 0,
    this.completedTodayCount = 0,
    this.activities = const [],
  });

  final int overdueCount;
  final int dueTodayCount;
  final int completedTodayCount;
  final List<ActivityDto> activities;

  factory MyDaySummaryDto.fromJson(Map<String, dynamic> json) {
    final list = (json['activities'] as List<dynamic>?)
            ?.map((e) => ActivityDto.fromJson(e as Map<String, dynamic>))
            .toList() ??
        [];
    return MyDaySummaryDto(
      overdueCount: (json['overdueCount'] as num?)?.toInt() ?? 0,
      dueTodayCount: (json['dueTodayCount'] as num?)?.toInt() ?? 0,
      completedTodayCount: (json['completedTodayCount'] as num?)?.toInt() ?? 0,
      activities: list,
    );
  }
}

