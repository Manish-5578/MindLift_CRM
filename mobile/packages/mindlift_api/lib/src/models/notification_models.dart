class NotificationItemDto {
  const NotificationItemDto({
    required this.id,
    required this.title,
    required this.message,
    this.type = 'General',
    this.isRead = false,
    required this.createdAtUtc,
    this.entityType,
    this.entityId,
    this.targetRoute,
  });

  final String id;
  final String title;
  final String message;
  final String type;
  final bool isRead;
  final DateTime createdAtUtc;
  final String? entityType;
  final String? entityId;
  final String? targetRoute;

  factory NotificationItemDto.fromJson(Map<String, dynamic> json) {
    final rawDate = json['createdAtUtc'] ?? json['createdAt'];
    DateTime dt = DateTime.now().toUtc();
    if (rawDate is String) {
      dt = DateTime.tryParse(rawDate) ?? DateTime.now().toUtc();
    }
    return NotificationItemDto(
      id: json['id'] as String? ?? '',
      title: json['title'] as String? ?? '',
      message: (json['body'] ?? json['message'] ?? '') as String,
      type: json['type'] as String? ?? 'General',
      isRead: json['isRead'] as bool? ?? false,
      createdAtUtc: dt,
      entityType: json['entityType'] as String?,
      entityId: json['entityId'] as String?,
      targetRoute: (json['url'] ?? json['targetRoute']) as String?,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'message': message,
        'type': type,
        'isRead': isRead,
        'createdAtUtc': createdAtUtc.toIso8601String(),
        if (entityType != null) 'entityType': entityType,
        if (entityId != null) 'entityId': entityId,
        if (targetRoute != null) 'targetRoute': targetRoute,
      };
}

