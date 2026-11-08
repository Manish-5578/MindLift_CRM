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

class LeadDuplicateCandidateDto {
  const LeadDuplicateCandidateDto({
    required this.id,
    required this.name,
    this.company,
    this.email,
    this.phone,
    this.recordType = 'Lead',
  });

  final String id;
  final String name;
  final String? company;
  final String? email;
  final String? phone;
  final String recordType;

  factory LeadDuplicateCandidateDto.fromJson(Map<String, dynamic> json) {
    return LeadDuplicateCandidateDto(
      id: json['id'] as String? ?? '',
      name: json['name'] as String? ?? 'Existing Contact',
      company: json['company'] as String?,
      email: json['email'] as String?,
      phone: json['phone'] as String?,
      recordType: json['recordType'] as String? ?? 'Lead',
    );
  }
}

class CardEvidenceBoxDto {
  const CardEvidenceBoxDto({
    required this.text,
    required this.confidence,
    required this.x,
    required this.y,
    required this.width,
    required this.height,
  });

  final String text;
  final double confidence;
  final double x;
  final double y;
  final double width;
  final double height;

  factory CardEvidenceBoxDto.fromJson(Map<String, dynamic> json) {
    return CardEvidenceBoxDto(
      text: json['text'] as String? ?? '',
      confidence: (json['confidence'] as num?)?.toDouble() ?? 0.0,
      x: (json['x'] as num?)?.toDouble() ?? 0.0,
      y: (json['y'] as num?)?.toDouble() ?? 0.0,
      width: (json['width'] as num?)?.toDouble() ?? 0.0,
      height: (json['height'] as num?)?.toDouble() ?? 0.0,
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
    this.overallConfidence = 0.9,
    this.companyConfidence,
    this.nameConfidence,
    this.emailConfidence,
    this.phoneConfidence,
    this.titleConfidence,
    this.detectedScript = 'Latin',
    this.duplicate,
    this.engineConfidence,
    this.evidence,
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
  final double overallConfidence;
  final double? companyConfidence;
  final double? nameConfidence;
  final double? emailConfidence;
  final double? phoneConfidence;
  final double? titleConfidence;
  final String detectedScript;
  final LeadDuplicateCandidateDto? duplicate;
  final double? engineConfidence;
  final List<CardEvidenceBoxDto>? evidence;

  factory ExtractedLeadCardDto.fromJson(Map<String, dynamic> json) {
    final evidenceRaw = json['evidence'];
    List<CardEvidenceBoxDto>? evidenceList;
    if (evidenceRaw is List) {
      evidenceList = evidenceRaw
          .map((e) => CardEvidenceBoxDto.fromJson(e as Map<String, dynamic>))
          .toList();
    }

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
      overallConfidence: (json['overallConfidence'] as num?)?.toDouble() ?? 0.85,
      companyConfidence: (json['companyConfidence'] as num?)?.toDouble(),
      nameConfidence: (json['nameConfidence'] as num?)?.toDouble(),
      emailConfidence: (json['emailConfidence'] as num?)?.toDouble(),
      phoneConfidence: (json['phoneConfidence'] as num?)?.toDouble(),
      titleConfidence: (json['titleConfidence'] as num?)?.toDouble(),
      detectedScript: json['detectedScript'] as String? ?? 'Latin',
      duplicate: json['duplicate'] != null
          ? LeadDuplicateCandidateDto.fromJson(json['duplicate'] as Map<String, dynamic>)
          : null,
      engineConfidence: (json['engineConfidence'] as num?)?.toDouble(),
      evidence: evidenceList,
    );
  }
}

