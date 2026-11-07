class LeadDto {
  const LeadDto({
    required this.id,
    required this.name,
    this.company,
    this.email,
    this.phone,
    this.status = 'New',
    this.rating,
    this.source,
    this.createdBy,
    this.createdAtUtc,
    this.notesCount = 0,
    this.version = 1,
  });

  final String id;
  final String name;
  final String? company;
  final String? email;
  final String? phone;
  final String status;
  final String? rating;
  final String? source;
  final String? createdBy;
  final DateTime? createdAtUtc;
  final int notesCount;
  final int version;

  factory LeadDto.fromJson(Map<String, dynamic> json) {
    return LeadDto(
      id: json['id'] as String? ?? '',
      name: json['name'] as String? ?? '',
      company: json['company'] as String?,
      email: json['email'] as String?,
      phone: json['phone'] as String?,
      status: json['status'] as String? ?? 'New',
      rating: json['rating'] as String?,
      source: json['source'] as String?,
      createdBy: json['createdBy'] as String? ?? json['owner'] as String?,
      createdAtUtc: json['createdAtUtc'] != null
          ? DateTime.tryParse(json['createdAtUtc'] as String)
          : null,
      notesCount: (json['notesCount'] as num?)?.toInt() ?? 0,
      version: (json['version'] as num?)?.toInt() ?? 1,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        if (company != null) 'company': company,
        if (email != null) 'email': email,
        if (phone != null) 'phone': phone,
        'status': status,
        if (rating != null) 'rating': rating,
        if (source != null) 'source': source,
        if (createdBy != null) 'createdBy': createdBy,
        if (createdAtUtc != null) 'createdAtUtc': createdAtUtc!.toIso8601String(),
        'notesCount': notesCount,
        'version': version,
      };
}

class LeadNoteDto {
  const LeadNoteDto({
    required this.id,
    required this.leadId,
    required this.text,
    required this.authorName,
    required this.createdAtUtc,
  });

  final String id;
  final String leadId;
  final String text;
  final String authorName;
  final DateTime createdAtUtc;

  factory LeadNoteDto.fromJson(Map<String, dynamic> json) {
    return LeadNoteDto(
      id: json['id'] as String? ?? '',
      leadId: json['leadId'] as String? ?? '',
      text: json['text'] as String? ?? json['note'] as String? ?? '',
      authorName: json['authorName'] as String? ?? json['author'] as String? ?? 'Unknown',
      createdAtUtc: json['createdAtUtc'] != null
          ? DateTime.parse(json['createdAtUtc'] as String)
          : DateTime.now().toUtc(),
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'leadId': leadId,
        'text': text,
        'authorName': authorName,
        'createdAtUtc': createdAtUtc.toIso8601String(),
      };
}

class LeadQueryRequest {
  const LeadQueryRequest({
    this.page = 1,
    this.pageSize = 20,
    this.searchTerm,
    this.status,
    this.source,
  });

  final int page;
  final int pageSize;
  final String? searchTerm;
  final String? status;
  final String? source;

  Map<String, dynamic> toJson() => {
        'page': page,
        'pageSize': pageSize,
        if (searchTerm != null && searchTerm!.isNotEmpty) 'searchTerm': searchTerm,
        if (status != null && status!.isNotEmpty) 'status': status,
        if (source != null && source!.isNotEmpty) 'source': source,
      };
}

class CreateLeadRequest {
  const CreateLeadRequest({
    required this.name,
    this.company,
    this.email,
    this.phone,
    this.source = 'Mobile',
    this.status = 'New',
    this.rating,
    this.notes,
  });

  final String name;
  final String? company;
  final String? email;
  final String? phone;
  final String source;
  final String status;
  final String? rating;
  final String? notes;

  Map<String, dynamic> toJson() => {
        'name': name,
        if (company != null) 'company': company,
        if (email != null) 'email': email,
        if (phone != null) 'phone': phone,
        'source': source,
        'status': status,
        if (rating != null) 'rating': rating,
        if (notes != null) 'notes': notes,
      };
}

class UpdateLeadStatusRequest {
  const UpdateLeadStatusRequest({required this.status});
  final String status;

  Map<String, dynamic> toJson() => {'status': status};
}

class AddLeadNoteRequest {
  const AddLeadNoteRequest({required this.note});
  final String note;

  Map<String, dynamic> toJson() => {'note': note};
}
