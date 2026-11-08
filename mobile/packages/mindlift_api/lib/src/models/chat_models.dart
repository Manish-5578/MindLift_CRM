class ChatMessageDto {
  const ChatMessageDto({
    required this.id,
    required this.senderId,
    required this.senderName,
    this.senderEmail,
    this.recipientId,
    this.channelName,
    required this.message,
    required this.createdAtUtc,
    this.isRead = false,
  });

  final String id;
  final String senderId;
  final String senderName;
  final String? senderEmail;
  final String? recipientId;
  final String? channelName;
  final String message;
  final DateTime createdAtUtc;
  final bool isRead;

  factory ChatMessageDto.fromJson(Map<String, dynamic> json) {
    final rawDate = json['createdAtUtc'] ?? json['createdAt'];
    DateTime dt = DateTime.now().toUtc();
    if (rawDate is String) {
      dt = DateTime.tryParse(rawDate) ?? DateTime.now().toUtc();
    }
    return ChatMessageDto(
      id: json['id'] as String? ?? '',
      senderId: json['senderId'] as String? ?? '',
      senderName: json['senderName'] as String? ?? 'Colleague',
      senderEmail: json['senderEmail'] as String?,
      recipientId: json['recipientId'] as String?,
      channelName: json['channelName'] as String?,
      message: json['message'] as String? ?? '',
      createdAtUtc: dt,
      isRead: json['isRead'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'senderId': senderId,
        'senderName': senderName,
        if (senderEmail != null) 'senderEmail': senderEmail,
        if (recipientId != null) 'recipientId': recipientId,
        if (channelName != null) 'channelName': channelName,
        'message': message,
        'createdAtUtc': createdAtUtc.toIso8601String(),
        'isRead': isRead,
      };
}

class ChatUserDto {
  const ChatUserDto({
    required this.id,
    required this.name,
    required this.email,
    this.role,
    this.unreadCount = 0,
    this.isOnline = true,
  });

  final String id;
  final String name;
  final String email;
  final String? role;
  final int unreadCount;
  final bool isOnline;

  factory ChatUserDto.fromJson(Map<String, dynamic> json) {
    return ChatUserDto(
      id: json['id'] as String? ?? '',
      name: json['name'] as String? ?? 'Team Member',
      email: json['email'] as String? ?? '',
      role: json['role'] as String? ?? 'Staff',
      unreadCount: json['unreadCount'] as int? ?? 0,
      isOnline: json['isOnline'] as bool? ?? true,
    );
  }
}

class ExtractedLeadCardDto {
  const ExtractedLeadCardDto({
    this.companyName,
    this.firstName,
    this.lastName,
    this.fullName,
    this.email,
    this.phone,
    this.jobTitle,
    this.website,
    this.address,
    required this.rawText,
  });

  final String? companyName;
  final String? firstName;
  final String? lastName;
  final String? fullName;
  final String? email;
  final String? phone;
  final String? jobTitle;
  final String? website;
  final String? address;
  final String rawText;

  factory ExtractedLeadCardDto.fromJson(Map<String, dynamic> json) {
    return ExtractedLeadCardDto(
      companyName: json['companyName'] as String?,
      firstName: json['firstName'] as String?,
      lastName: json['lastName'] as String?,
      fullName: json['fullName'] as String?,
      email: json['email'] as String?,
      phone: json['phone'] as String?,
      jobTitle: json['jobTitle'] as String?,
      website: json['website'] as String?,
      address: json['address'] as String?,
      rawText: json['rawText'] as String? ?? '',
    );
  }
}

