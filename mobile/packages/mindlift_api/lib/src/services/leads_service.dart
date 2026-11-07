import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/lead_models.dart';

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

  Future<void> updateLeadStatus(String id, String status) async {
    await _dio.put(
      '/api/v1/leads/$id/status',
      data: UpdateLeadStatusRequest(status: status).toJson(),
    );
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
}
