import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/chat_models.dart';

class ChatService {
  const ChatService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<List<ChatMessageDto>> getMessages({
    String? channel,
    String? recipientId,
    DateTime? since,
  }) async {
    final response = await _dio.get(
      '/api/v1/chat/messages',
      queryParameters: {
        if (channel != null) 'channel': channel,
        if (recipientId != null) 'recipientId': recipientId,
        if (since != null) 'since': since.toIso8601String(),
      },
    );
    final data = response.data;
    if (data is List) {
      return data.map((e) => ChatMessageDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<ChatMessageDto> sendMessage({
    required String message,
    String? channel,
    String? recipientId,
  }) async {
    final response = await _dio.post(
      '/api/v1/chat/messages',
      data: {
        'message': message,
        if (channel != null) 'channelName': channel,
        if (recipientId != null) 'recipientId': recipientId,
      },
    );
    return ChatMessageDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<List<ChatUserDto>> getUsers() async {
    final response = await _dio.get('/api/v1/chat/users');
    final data = response.data;
    if (data is List) {
      return data.map((e) => ChatUserDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<void> markRead({String? otherUserId, String? channel}) async {
    await _dio.post(
      '/api/v1/chat/mark-read',
      queryParameters: {
        if (otherUserId != null) 'otherUserId': otherUserId,
        if (channel != null) 'channel': channel,
      },
    );
  }

  Future<ExtractedLeadCardDto> scanCardFile({
    required List<int> bytes,
    required String fileName,
  }) async {
    final formData = FormData.fromMap({
      'file': MultipartFile.fromBytes(
        bytes,
        filename: fileName,
      ),
    });
    final response = await _dio.post(
      '/api/v1/leads/scan-card-file',
      data: formData,
      options: Options(
        contentType: 'multipart/form-data',
      ),
    );
    return ExtractedLeadCardDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<ExtractedLeadCardDto> scanCardFilePath({
    required String filePath,
    required String fileName,
  }) async {
    final formData = FormData.fromMap({
      'file': await MultipartFile.fromFile(
        filePath,
        filename: fileName,
      ),
    });
    final response = await _dio.post(
      '/api/v1/leads/scan-card-file',
      data: formData,
      options: Options(
        contentType: 'multipart/form-data',
      ),
    );
    return ExtractedLeadCardDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<ExtractedLeadCardDto> parseLeadCard({String? rawText, String? imageBase64}) async {
    final response = await _dio.post(
      '/api/v1/leads/parse-card',
      data: {
        if (rawText != null) 'rawText': rawText,
        if (imageBase64 != null) 'imageBase64': imageBase64,
      },
    );
    return ExtractedLeadCardDto.fromJson(response.data as Map<String, dynamic>);
  }
}

