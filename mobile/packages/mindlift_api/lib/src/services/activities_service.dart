import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/activity_models.dart';

class ActivitiesService {
  const ActivitiesService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<MyDaySummaryDto> getMyDay() async {
    final response = await _dio.get('/api/v1/activities/my-day');
    return MyDaySummaryDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<List<ActivityDto>> listActivities({
    String? type,
    String? status,
    String? leadId,
  }) async {
    // The API lists activities with POST /activities/query and a tab: open ones are today / overdue / upcoming.
    final response = await _dio.post('/api/v1/activities/query', data: {
      'tab': status == 'Completed' ? 'completed' : 'all',
      if (type != null) 'type': type,
      'mineOnly': leadId == null,
      if (leadId != null) 'leadId': leadId,
      'page': 1,
      'pageSize': 100,
    });
    final data = response.data;
    if (data is Map && data['items'] is List) {
      final all = (data['items'] as List).whereType<Map>().map((e) => ActivityDto.fromJson(Map<String, dynamic>.from(e))).toList();
      return status == 'Open' ? all.where((a) => a.status != 'Completed').toList() : all;
    }
    return [];
  }

  Future<ActivityDto> createActivity(CreateActivityRequest request) async {
    final response = await _dio.post(
      '/api/v1/activities',
      data: request.toJson(),
    );
    return ActivityDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<void> completeActivity(String id, {String? outcome, String? note, int? durationMinutes}) async {
    await _dio.post(
      '/api/v1/activities/$id/complete',
      data: CompleteActivityRequest(
        outcome: outcome,
        note: note,
        durationMinutes: durationMinutes,
      ).toJson(),
    );
  }

  Future<void> rescheduleActivity(String id, DateTime newDueAtUtc, {String? reason}) async {
    await _dio.post(
      '/api/v1/activities/$id/reschedule',
      data: RescheduleActivityRequest(
        newDueAtUtc: newDueAtUtc,
        reason: reason,
      ).toJson(),
    );
  }
}

