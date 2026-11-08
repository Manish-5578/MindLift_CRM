import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/lead_models.dart';
import '../models/lead_workspace_models.dart';

class LeadsService {
  const LeadsService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<List<LeadDto>> queryLeads(LeadQueryRequest query) async {
    final response = await _dio.post(
      '/api/v1/leads/query',
      data: query.toJson(),
    );
    final data = response.data;
    if (data is List) {
      return data.map((e) => LeadDto.fromJson(e as Map<String, dynamic>)).toList();
    } else if (data is Map && data['items'] is List) {
      return (data['items'] as List)
          .map((e) => LeadDto.fromJson(e as Map<String, dynamic>))
          .toList();
    }
    return [];
  }

  Future<LeadDto> getLead(String id) async {
    final response = await _dio.get('/api/v1/leads/$id');
    return LeadDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<LeadDto> createLead(CreateLeadRequest request) async {
    final response = await _dio.post(
      '/api/v1/leads',
      data: request.toJson(),
    );
    return LeadDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// Same call as the web app: POST /leads/{id}/status.
  Future<void> updateLeadStatus(String id, String status) async {
    await _dio.post('/api/v1/leads/$id/status', data: {'status': status});
  }

  Future<List<LeadNoteDto>> getLeadNotes(String leadId) async {
    final response = await _dio.get('/api/v1/leads/$leadId/notes');
    final data = response.data;
    if (data is List) {
      return data.map((e) => LeadNoteDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<LeadNoteDto> addLeadNote(String leadId, String note) async {
    final response = await _dio.post(
      '/api/v1/leads/$leadId/notes',
      data: AddLeadNoteRequest(note: note).toJson(),
    );
    return LeadNoteDto.fromJson(response.data as Map<String, dynamic>);
  }
  // ---- Leads workspace: the same calls the web leads list makes ----

  Future<LeadWorkspaceConfig> loadWorkspaceConfig() async {
    final response = await _dio.get('/api/v1/app/bootstrap');
    return LeadWorkspaceConfig.fromBootstrap(Map<String, dynamic>.from(response.data as Map));
  }

  Future<List<LeadView>> loadSavedViews() async {
    try {
      final response = await _dio.get('/api/v1/views/leads');
      final data = response.data;
      if (data is List) {
        return data.whereType<Map>().map((e) => LeadView.fromSaved(Map<String, dynamic>.from(e))).toList();
      }
    } catch (_) {}
    return const [];
  }

  Future<LeadPage> queryPage({
    String? search,
    Map<String, dynamic>? filter,
    String sortField = 'created_at',
    String sortDir = 'desc',
    int page = 1,
    int pageSize = 30,
  }) async {
    final response = await _dio.post('/api/v1/leads/query', data: {
      if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
      if (filter != null) 'filter': filter,
      'sortField': sortField,
      'sortDir': sortDir,
      'page': page,
      'pageSize': pageSize,
    });
    final data = Map<String, dynamic>.from(response.data as Map);
    final items = ((data['items'] as List?) ?? const [])
        .whereType<Map>()
        .map((e) => LeadRow.fromJson(Map<String, dynamic>.from(e)))
        .toList();
    return LeadPage(
      items: items,
      total: (data['total'] as num?)?.toInt() ?? items.length,
      page: (data['page'] as num?)?.toInt() ?? page,
      pageSize: (data['pageSize'] as num?)?.toInt() ?? pageSize,
    );
  }

  Future<Map<String, dynamic>> getLeadRaw(String id) async {
    final response = await _dio.get('/api/v1/leads/$id');
    return Map<String, dynamic>.from(response.data as Map);
  }

  Future<void> disqualify(String id, String reason) async {
    await _dio.post('/api/v1/leads/$id/disqualify', data: {'reason': reason, 'notes': null});
  }

  Future<void> setStickyNote(String id, String? note) async {
    await _dio.patch('/api/v1/leads/$id/sticky-note', data: {'note': note});
  }

  /// Returns the new deal id when the server created one.
  Future<String?> quickConvertToEngagement(String id) async {
    final response = await _dio.post('/api/v1/leads/$id/quick-convert-engagement', data: <String, dynamic>{});
    final data = response.data;
    return data is Map ? data['dealId'] as String? : null;
  }

  Future<void> quickConvertToContact(String id) async {
    await _dio.post('/api/v1/leads/$id/quick-convert-contact', data: <String, dynamic>{});
  }

  Future<void> deleteLead(String id) async {
    await _dio.delete('/api/v1/leads/$id');
  }

  /// Saves the full lead (edit) or creates one when [id] is null. [fields] uses the web API names.
  Future<Map<String, dynamic>> saveLead(String? id, Map<String, dynamic> fields) async {
    final response = id == null
        ? await _dio.post('/api/v1/leads', data: fields)
        : await _dio.put('/api/v1/leads/$id', data: fields);
    return Map<String, dynamic>.from(response.data as Map);
  }

  Future<void> bulkChangeStatus(List<String> ids, String status) async {
    await _dio.post('/api/v1/leads/bulk', data: {'action': 'status', 'ids': ids, 'status': status});
  }

  Future<void> bulkDelete(List<String> ids) async {
    await _dio.post('/api/v1/leads/bulk-delete', data: {'ids': ids});
  }

  /// Move leads into another view (a built-in one or a saved one), as the web "Change view" does.
  Future<void> moveToView(List<String> ids, String targetViewKey, String? sourceViewKey) async {
    await _dio.post('/api/v1/views/move-record', data: {
      'entityType': 'leads',
      'recordId': ids.first,
      'targetViewKey': targetViewKey,
      'sourceViewKey': sourceViewKey,
      'recordIds': ids,
    });
  }

  Future<void> deleteLeadNote(String leadId, String noteId) async {
    await _dio.delete('/api/v1/leads/$leadId/notes/$noteId');
  }
}
