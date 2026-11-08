/// A row of the leads list (same fields as the web list).
class LeadRow {
  const LeadRow({
    required this.id,
    required this.fullName,
    required this.firstName,
    required this.lastName,
    this.email,
    this.phone,
    this.companyName,
    required this.status,
    this.source,
    this.rating,
    this.ownerName,
    this.createdByName,
    required this.createdAtUtc,
    this.isConverted = false,
    this.isDisqualified = false,
    this.address,
    this.description,
    this.city,
    this.state,
  });

  final String id;
  final String fullName;
  final String firstName;
  final String lastName;
  final String? email;
  final String? phone;
  final String? companyName;
  final String status;
  final String? source;
  final String? rating;
  final String? ownerName;
  final String? createdByName;
  final DateTime createdAtUtc;
  final bool isConverted;
  final bool isDisqualified;
  final String? address;
  final String? description;
  final String? city;
  final String? state;

  String get title => (companyName != null && companyName!.trim().isNotEmpty)
      ? companyName!.trim()
      : (fullName.trim().isNotEmpty ? fullName.trim() : '—');

  String get subtitle => fullName.trim().isNotEmpty ? fullName.trim() : (email ?? phone ?? '—');

  LeadRow copyWith({String? status, String? description}) => LeadRow(
        id: id,
        fullName: fullName,
        firstName: firstName,
        lastName: lastName,
        email: email,
        phone: phone,
        companyName: companyName,
        status: status ?? this.status,
        source: source,
        rating: rating,
        ownerName: ownerName,
        createdByName: createdByName,
        createdAtUtc: createdAtUtc,
        isConverted: isConverted,
        isDisqualified: isDisqualified,
        address: address,
        description: description ?? this.description,
        city: city,
        state: state,
      );

  factory LeadRow.fromJson(Map<String, dynamic> j) => LeadRow(
        id: j['id'] as String? ?? '',
        fullName: j['fullName'] as String? ?? '',
        firstName: j['firstName'] as String? ?? '',
        lastName: j['lastName'] as String? ?? '',
        email: j['email'] as String?,
        phone: j['phone'] as String?,
        companyName: j['companyName'] as String?,
        status: j['status'] as String? ?? 'new',
        source: j['source'] as String?,
        rating: j['rating'] as String?,
        ownerName: j['ownerName'] as String?,
        createdByName: j['createdByName'] as String?,
        createdAtUtc: DateTime.tryParse(j['createdAtUtc'] as String? ?? '') ?? DateTime.now().toUtc(),
        isConverted: j['isConverted'] as bool? ?? false,
        isDisqualified: j['isDisqualified'] as bool? ?? false,
        address: j['address'] as String?,
        description: j['description'] as String?,
        city: j['city'] as String?,
        state: j['state'] as String?,
      );
}

class LeadPage {
  const LeadPage({required this.items, required this.total, required this.page, required this.pageSize});
  final List<LeadRow> items;
  final int total;
  final int page;
  final int pageSize;
  bool get hasMore => page * pageSize < total;
}

class PicklistOption {
  const PicklistOption({required this.value, required this.label, this.labelAr, this.isActive = true, this.sortOrder = 0});
  final String value;
  final String label;
  final String? labelAr;
  final bool isActive;
  final int sortOrder;

  factory PicklistOption.fromJson(Map<String, dynamic> j) => PicklistOption(
        value: j['value'] as String? ?? '',
        label: j['label'] as String? ?? (j['value'] as String? ?? ''),
        labelAr: j['labelAr'] as String?,
        isActive: j['isActive'] as bool? ?? true,
        sortOrder: (j['sortOrder'] as num?)?.toInt() ?? 0,
      );
}

/// What the web app gets at login (bootstrap): picklists and what this user may do with leads.
class LeadWorkspaceConfig {
  const LeadWorkspaceConfig({
    this.statuses = const [],
    this.sources = const [],
    this.userId,
    this.isAdmin = false,
    this.canCreate = true,
    this.canEdit = true,
    this.canDelete = false,
    this.canExport = false,
    this.canImport = false,
  });

  final List<PicklistOption> statuses;
  final List<PicklistOption> sources;
  final String? userId;
  final bool isAdmin;
  final bool canCreate;
  final bool canEdit;
  final bool canDelete;
  final bool canExport;
  final bool canImport;

  /// Statuses a lead can be set to by hand (converting is its own step).
  List<PicklistOption> get settableStatuses {
    final list = statuses.where((s) => s.value != 'converted').toList();
    if (list.isNotEmpty) return list;
    return const [
      PicklistOption(value: 'new', label: 'New'),
      PicklistOption(value: 'working', label: 'Contacted', sortOrder: 1),
      PicklistOption(value: 'qualified', label: 'Qualified', sortOrder: 2),
      PicklistOption(value: 'disqualified', label: 'Not interested', sortOrder: 3),
    ];
  }

  String statusLabel(String code) => settableStatuses
      .followedBy(statuses)
      .firstWhere((s) => s.value == code, orElse: () => PicklistOption(value: code, label: _pretty(code)))
      .label;

  String sourceLabel(String? code) => code == null
      ? '—'
      : sources.firstWhere((s) => s.value == code, orElse: () => PicklistOption(value: code, label: _pretty(code))).label;

  static String _pretty(String c) => c.isEmpty ? c : c[0].toUpperCase() + c.substring(1).replaceAll('_', ' ');

  factory LeadWorkspaceConfig.fromBootstrap(Map<String, dynamic> j) {
    List<PicklistOption> pick(String key) {
      final lists = (j['picklists'] as List?) ?? const [];
      for (final l in lists) {
        if (l is Map && l['key'] == key) {
          final vals = ((l['values'] as List?) ?? const [])
              .whereType<Map>()
              .map((e) => PicklistOption.fromJson(Map<String, dynamic>.from(e)))
              .where((v) => v.isActive)
              .toList()
            ..sort((a, b) => a.sortOrder.compareTo(b.sortOrder));
          return vals;
        }
      }
      return const [];
    }

    final perms = ((j['permissions'] as List?) ?? const []).whereType<Map>().where((p) => p['module'] == 'Leads');
    final p = perms.isEmpty ? null : perms.first;
    final user = j['user'] is Map ? Map<String, dynamic>.from(j['user'] as Map) : const <String, dynamic>{};
    return LeadWorkspaceConfig(
      statuses: pick('lead_status'),
      sources: pick('lead_source'),
      userId: user['userId'] as String?,
      isAdmin: user['isAdmin'] == true,
      canCreate: p == null ? true : p['canCreate'] == true,
      canEdit: p == null ? true : p['canEdit'] == true,
      canDelete: p == null ? false : p['canDelete'] == true,
      canExport: p == null ? false : p['canExport'] == true,
      canImport: p == null ? false : p['canImport'] == true,
    );
  }
}

/// A tab above the list: a built-in filter, or a view someone saved on the web.
class LeadView {
  const LeadView({required this.key, required this.label, this.filter, this.savedId, this.sortField, this.sortDir});
  final String key;
  final String label;
  final Map<String, dynamic>? filter;
  final String? savedId;
  final String? sortField;
  final String? sortDir;

  // Operator codes of the shared filter model (the enum order on the server).
  static const int opEq = 0;
  static const int opIn = 12;
  static const int opNotIn = 13;

  static Map<String, dynamic> _cond(String field, int op, Object value) => {'field': field, 'op': op, 'value': value};
  static Map<String, dynamic> _group(List<Map<String, dynamic>> c) => {'logic': 'and', 'conditions': c, 'groups': <dynamic>[]};

  /// The same five built-in views as the web list.
  static List<LeadView> builtIn() => [
        LeadView(key: 'mine', label: 'My open leads', filter: _group([
          _cond('owner_id', opEq, '@me'),
          _cond('status', opIn, ['new', 'working', 'qualified']),
        ])),
        LeadView(key: 'all', label: 'All open', filter: _group([
          _cond('status', opNotIn, ['disqualified']),
        ])),
        LeadView(key: 'new', label: 'New', filter: _group([_cond('status', opEq, 'new')])),
        LeadView(key: 'disqualified', label: 'Not interested', filter: _group([_cond('status', opEq, 'disqualified')])),
        LeadView(key: 'all_leads', label: 'All leads', filter: _group([])),
      ];

  factory LeadView.fromSaved(Map<String, dynamic> j) {
    final id = (j['id'] as String? ?? '');
    return LeadView(
      key: 'saved:${id.replaceAll('-', '')}',
      label: j['name'] as String? ?? 'View',
      filter: j['filter'] is Map ? Map<String, dynamic>.from(j['filter'] as Map) : null,
      savedId: id,
      sortField: j['sortField'] as String?,
      sortDir: j['sortDir'] as String?,
    );
  }
}
