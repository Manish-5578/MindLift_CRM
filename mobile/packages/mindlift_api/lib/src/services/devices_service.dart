import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/mobile_device_models.dart';

class MobileDevicesService {
  const MobileDevicesService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<void> registerCurrentDevice(RegisterMobileDeviceRequest request) async {
    await _dio.put(
      '/api/v1/mobile/devices/current',
      data: request.toJson(),
    );
  }

  /// Asks the server to push a test notification to this user's devices; returns what it did per device.
  Future<List<String>> sendTestPush() async {
    final response = await _dio.post('/api/v1/mobile/devices/current/test-push');
    final data = response.data;
    if (data is Map && data['results'] is List) {
      return (data['results'] as List).map((e) => e.toString()).toList();
    }
    return const [];
  }

  Future<void> revokeCurrentDevice(String installationId) async {
    await _dio.delete(
      '/api/v1/mobile/devices/current',
      data: {'installationId': installationId},
    );
  }

  Future<void> sendHeartbeat(MobileDeviceHeartbeatRequest request) async {
    await _dio.post(
      '/api/v1/mobile/devices/current/heartbeat',
      data: request.toJson(),
    );
  }
}

