import 'package:dio/dio.dart';
import '../client/api_client.dart';

/// One page of records from a list endpoint, kept as plain maps so a saved copy can be shown offline.
class RecordPage {
  const RecordPage({required this.items, required this.total, required this.page, required this.pageSize});
  final List<Map<String, dynamic>> items;
  final int total;
  final int page;
  final int pageSize;
  bool get hasMore => page * pageSize < total;

  Map<String, dynamic> toJson() => {'items': items, 'total': total, 'page': page, 'pageSize': pageSize};

  factory RecordPage.fromJson(Map<String, dynamic> j) => RecordPage(
        items: ((j['items'] as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList(),
        total: (j['total'] as num?)?.toInt() ?? 0,
        page: (j['page'] as num?)?.toInt() ?? 1,
        pageSize: (j['pageSize'] as num?)?.toInt() ?? 30,
      );
}

/// Contacts, accounts, deals, the calendar, documents, profile photos and push settings:
/// the endpoints the web app uses, called exactly as the web app does.
class WorkspaceService {
  const WorkspaceService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Map<String, dynamic> _map(Object? data) => Map<String, dynamic>.from(data as Map);

  // ---- lists

  /// [entity] is contacts, accounts or deals.
  Future<RecordPage> query(String entity, {String? search, Map<String, dynamic>? filter, String sortField = 'created_at', String sortDir = 'desc', int page = 1, int pageSize = 30}) async {
    final response = await _dio.post('/api/v1/$entity/query', data: {
      if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
      if (filter != null) 'filter': filter,
      'sortField': sortField,
      'sortDir': sortDir,
      'page': page,
      'pageSize': pageSize,
    });
    return RecordPage.fromJson(_map(response.data));
  }

  Future<Map<String, dynamic>> summary(String entity, String id) async {
    final response = await _dio.get('/api/v1/$entity/$id/summary');
    return _map(response.data);
  }

  Future<Map<String, dynamic>> getRecord(String entity, String id) async {
    final response = await _dio.get('/api/v1/$entity/$id');
    return _map(response.data);
  }

  Future<Map<String, dynamic>> createContact({required String firstName, String lastName = '', String? email, String? phone, String? jobTitle, String? accountId}) async {
    final response = await _dio.post('/api/v1/contacts', data: {
      'firstName': firstName,
      'lastName': lastName,
      'email': email,
      'phone': phone,
      'jobTitle': jobTitle,
      'accountId': accountId,
    });
    return _map(response.data);
  }

  Future<Map<String, dynamic>> createAccount({required String name, String? website, String? phone, String? industry}) async {
    final response = await _dio.post('/api/v1/accounts', data: {'name': name, 'website': website, 'phone': phone, 'industry': industry});
    return _map(response.data);
  }

  /// Edits a contact. The API replaces the whole record, so unchanged fields are sent back as they were.
  Future<Map<String, dynamic>> updateContact(Map<String, dynamic> current, {required String firstName, String lastName = '', String? email, String? phone, String? jobTitle, String? description}) async {
    final response = await _dio.put('/api/v1/contacts/${current['id']}', data: {
      'firstName': firstName,
      'lastName': lastName,
      'email': email,
      'phone': phone,
      'jobTitle': jobTitle,
      'description': description,
      'accountId': current['accountId'],
      'type': current['type'],
      'ownerId': current['ownerId'],
      'customFields': current['customFields'] ?? <String, dynamic>{},
    });
    return _map(response.data);
  }

  Future<Map<String, dynamic>> updateAccount(Map<String, dynamic> current, {required String name, String? website, String? phone, String? industry, String? description}) async {
    final response = await _dio.put('/api/v1/accounts/${current['id']}', data: {
      'name': name,
      'website': website,
      'phone': phone,
      'industry': industry,
      'description': description,
      'parentAccountId': current['parentAccountId'],
      'ownerId': current['ownerId'],
      'customFields': current['customFields'] ?? <String, dynamic>{},
    });
    return _map(response.data);
  }

  // ---- deals

  Future<List<Map<String, dynamic>>> pipelines() async {
    final response = await _dio.get('/api/v1/settings/pipelines');
    return ((response.data as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();
  }

  /// Edits a deal's own details; its stage, pipeline, people and owner stay as they were.
  Future<Map<String, dynamic>> updateDeal(Map<String, dynamic> current, {required String name, required double amount, String? currency, String? expectedCloseDate, String? description}) async {
    final contacts = ((current['contacts'] as List?) ?? const []).whereType<Map>().map((c) => {'contactId': c['contactId'], 'role': c['role'], 'isPrimary': c['isPrimary'] == true}).toList();
    final response = await _dio.put('/api/v1/deals/${current['id']}', data: {
      'name': name,
      'pipelineId': current['pipelineId'],
      'stageId': current['stageId'],
      'amount': amount,
      'currency': currency,
      'probability': current['probabilityOverridden'] == true ? current['probability'] : null,
      'expectedCloseDate': expectedCloseDate,
      'accountId': current['accountId'],
      'contacts': contacts,
      'description': description,
      'ownerId': current['ownerId'],
      'customFields': current['customFields'] ?? <String, dynamic>{},
    });
    return _map(response.data);
  }

  /// A new deal in the default pipeline's first open stage.
  Future<Map<String, dynamic>> createDeal({required String name, required double amount, String? currency, String? expectedCloseDate}) async {
    final pipelines = await this.pipelines();
    final pipeline = pipelines.firstWhere((p) => p['isDefault'] == true, orElse: () => pipelines.isEmpty ? <String, dynamic>{} : pipelines.first);
    final stages = ((pipeline['stages'] as List?) ?? const []).whereType<Map>().where((s) => s['stageType'] == 'Open').toList()
      ..sort((a, b) => ((a['sortOrder'] as num?) ?? 0).compareTo((b['sortOrder'] as num?) ?? 0));
    final response = await _dio.post('/api/v1/deals', data: {
      'name': name,
      'pipelineId': pipeline['id'],
      'stageId': stages.isEmpty ? null : stages.first['id'],
      'amount': amount,
      'currency': currency,
      'expectedCloseDate': expectedCloseDate,
    });
    return _map(response.data);
  }

  Future<Map<String, dynamic>> changeDealStage(String dealId, String stageId, {String? lostReason}) async {
    final response = await _dio.patch('/api/v1/deals/$dealId/stage', data: {'stageId': stageId, if (lostReason != null) 'lostReason': lostReason});
    return _map(response.data);
  }

  // ---- calendar and activities

  /// Every activity of the signed-in person (open and done), newest first. The calendar groups them by day.
  Future<RecordPage> myActivities({String tab = 'all', int page = 1, int pageSize = 200, String? type, bool mineOnly = true}) async {
    final response = await _dio.post('/api/v1/activities/query', data: {
      'tab': tab,
      if (type != null) 'type': type,
      'mineOnly': mineOnly,
      'page': page,
      'pageSize': pageSize,
    });
    return RecordPage.fromJson(_map(response.data));
  }

  Future<RecordPage> activitiesFor(String field, String id, {int pageSize = 50}) async {
    final response = await _dio.post('/api/v1/activities/query', data: {'tab': 'all', 'mineOnly': false, field: id, 'page': 1, 'pageSize': pageSize});
    return RecordPage.fromJson(_map(response.data));
  }

  Future<void> completeActivity(String id, {String? outcome, String? notes}) async {
    await _dio.post('/api/v1/activities/$id/complete', data: {'outcome': outcome, 'notes': notes, 'followUp': null});
  }

  // ---- documents

  Future<List<Map<String, dynamic>>> files(String entityType, String entityId) async {
    final response = await _dio.get('/api/v1/files', queryParameters: {'entityType': entityType, 'entityId': entityId});
    return ((response.data as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();
  }

  Future<Map<String, dynamic>> uploadFile(String entityType, String entityId, {required String path, required String name}) async {
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(path, filename: name),
      'entityType': entityType,
      'entityId': entityId,
    });
    final response = await _dio.post('/api/v1/files', data: form, options: Options(contentType: 'multipart/form-data', sendTimeout: const Duration(minutes: 2), receiveTimeout: const Duration(minutes: 2)));
    return _map(response.data);
  }

  Future<List<int>> downloadFile(String id) async {
    final response = await _dio.get<List<int>>('/api/v1/files/$id', options: Options(responseType: ResponseType.bytes, receiveTimeout: const Duration(minutes: 2)));
    return response.data ?? const <int>[];
  }

  // ---- profile photo

  Future<void> uploadAvatar({required String path, required String name}) async {
    final form = FormData.fromMap({'file': await MultipartFile.fromFile(path, filename: name)});
    await _dio.put('/api/v1/avatars/me', data: form, options: Options(contentType: 'multipart/form-data'));
  }

  Future<void> removeAvatar() async {
    await _dio.delete('/api/v1/avatars/me');
  }

  /// Null when the person has no photo.
  Future<List<int>?> avatar(String userId) async {
    try {
      final response = await _dio.get<List<int>>('/api/v1/avatars/$userId', options: Options(responseType: ResponseType.bytes));
      return response.data;
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  // ---- push settings

  Future<void> setQuietHours(String installationId, int? startMinute, int? endMinute) async {
    await _dio.put('/api/v1/mobile/devices/current/quiet-hours', data: {'installationId': installationId, 'startMinute': startMinute, 'endMinute': endMinute});
  }

  Future<Map<String, dynamic>> notificationPreferences() async {
    final response = await _dio.get('/api/v1/notifications/preferences');
    return _map(response.data);
  }

  Future<void> saveNotificationPreferences(List<Map<String, dynamic>> preferences, bool dailyDigest) async {
    await _dio.put('/api/v1/notifications/preferences', data: {'preferences': preferences, 'dailyDigest': dailyDigest});
  }
}
