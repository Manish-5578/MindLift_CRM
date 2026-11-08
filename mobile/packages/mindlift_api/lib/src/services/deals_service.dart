import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/deal_models.dart';

class DealsService {
  const DealsService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<List<DealDto>> listDeals({String? stage}) async {
    final response = await _dio.get(
      '/api/v1/deals',
      queryParameters: {
        if (stage != null) 'stage': stage,
      },
    );
    final data = response.data;
    if (data is List) {
      return data.map((e) => DealDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<DealDto> getDeal(String id) async {
    final response = await _dio.get('/api/v1/deals/$id');
    return DealDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<void> updateDealStage(String id, String newStage) async {
    await _dio.put(
      '/api/v1/deals/$id/stage',
      data: UpdateDealStageRequest(stage: newStage).toJson(),
    );
  }
}

