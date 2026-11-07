class DealDto {
  const DealDto({
    required this.id,
    required this.title,
    this.value = 0.0,
    this.currency = 'USD',
    this.stage = 'Lead',
    this.probability = 10,
    this.expectedCloseDate,
    this.leadId,
    this.contactId,
    this.assignedToName,
  });

  final String id;
  final String title;
  final double value;
  final String currency;
  final String stage; // Lead, Qualified, Proposal, Negotiation, Won, Lost
  final int probability;
  final DateTime? expectedCloseDate;
  final String? leadId;
  final String? contactId;
  final String? assignedToName;

  factory DealDto.fromJson(Map<String, dynamic> json) {
    return DealDto(
      id: json['id'] as String? ?? '',
      title: json['title'] as String? ?? '',
      value: (json['value'] as num?)?.toDouble() ?? 0.0,
      currency: json['currency'] as String? ?? 'USD',
      stage: json['stage'] as String? ?? 'Lead',
      probability: (json['probability'] as num?)?.toInt() ?? 10,
      expectedCloseDate: json['expectedCloseDate'] != null
          ? DateTime.tryParse(json['expectedCloseDate'] as String)
          : null,
      leadId: json['leadId'] as String?,
      contactId: json['contactId'] as String?,
      assignedToName: json['assignedToName'] as String?,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'value': value,
        'currency': currency,
        'stage': stage,
        'probability': probability,
        if (expectedCloseDate != null) 'expectedCloseDate': expectedCloseDate!.toIso8601String(),
        if (leadId != null) 'leadId': leadId,
        if (contactId != null) 'contactId': contactId,
        if (assignedToName != null) 'assignedToName': assignedToName,
      };
}

class UpdateDealStageRequest {
  const UpdateDealStageRequest({required this.stage});
  final String stage;

  Map<String, dynamic> toJson() => {'stage': stage};
}
