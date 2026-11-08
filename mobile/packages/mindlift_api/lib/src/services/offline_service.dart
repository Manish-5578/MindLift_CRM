import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/offline_models.dart';

class OfflineSyncService {
  const OfflineSyncService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<OfflineBootstrapDto> getBootstrapSnapshot() async {
    final response = await _dio.get('/api/v1/offline/bootstrap');
    return OfflineBootstrapDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<OfflineBatchSyncResultDto> submitBatchSync(OfflineBatchSyncRequest request) async {
    final response = await _dio.post(
      '/api/v1/offline/sync',
      data: request.toJson(),
    );
    return OfflineBatchSyncResultDto.fromJson(response.data as Map<String, dynamic>);
  }
}

