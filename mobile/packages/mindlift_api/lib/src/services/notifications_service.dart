import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/notification_models.dart';

class NotificationsService {
  const NotificationsService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<List<NotificationItemDto>> listNotifications({bool? unreadOnly}) async {
    final response = await _dio.get(
      '/api/v1/notifications',
      queryParameters: {
        if (unreadOnly != null) 'unreadOnly': unreadOnly,
      },
    );
    final data = response.data;
    if (data is List) {
      return data.map((e) => NotificationItemDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<void> markAsRead(String id) async {
    await _dio.post('/api/v1/notifications/$id/read');
  }

  Future<void> markAllAsRead() async {
    await _dio.post('/api/v1/notifications/read-all');
  }
}

