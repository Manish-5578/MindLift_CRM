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
    final response = await _dio.get(
      '/api/v1/activities',
      queryParameters: {
        if (type != null) 'type': type,
        if (status != null) 'status': status,
        if (leadId != null) 'leadId': leadId,
      },
    );
    final data = response.data;
    if (data is List) {
      return data.map((e) => ActivityDto.fromJson(e as Map<String, dynamic>)).toList();
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
