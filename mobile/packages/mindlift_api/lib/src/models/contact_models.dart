class ContactDto {
  const ContactDto({
    required this.id,
    required this.name,
    this.email,
    this.phone,
    this.company,
    this.title,
    this.leadId,
    this.accountId,
  });

  final String id;
  final String name;
  final String? email;
  final String? phone;
  final String? company;
  final String? title;
  final String? leadId;
  final String? accountId;

  factory ContactDto.fromJson(Map<String, dynamic> json) {
    return ContactDto(
      id: json['id'] as String? ?? '',
      name: json['name'] as String? ?? '',
      email: json['email'] as String?,
      phone: json['phone'] as String?,
      company: json['company'] as String?,
      title: json['title'] as String?,
      leadId: json['leadId'] as String?,
      accountId: json['accountId'] as String?,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        if (email != null) 'email': email,
        if (phone != null) 'phone': phone,
        if (company != null) 'company': company,
        if (title != null) 'title': title,
        if (leadId != null) 'leadId': leadId,
        if (accountId != null) 'accountId': accountId,
      };
}
