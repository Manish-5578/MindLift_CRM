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
    String? search,
  }) async {
    final response = await _dio.get(
      '/api/v1/chat/messages',
      queryParameters: {
        if (search != null && search.isNotEmpty) 'search': search,
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
    String? replyToId,
  }) async {
    final response = await _dio.post(
      '/api/v1/chat/messages',
      data: {
        'message': message,
        if (replyToId != null) 'replyToId': replyToId,
        if (channel != null) 'channelName': channel,
        if (recipientId != null) 'recipientId': recipientId,
      },
    );
    return ChatMessageDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// A photo, document or voice note, with an optional caption.
  Future<ChatMessageDto> sendAttachment({
    required String filePath,
    required String fileName,
    required String recipientId,
    String? caption,
    String? replyToId,
    int? durationMs,
  }) async {
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(filePath, filename: fileName),
      'recipientId': recipientId,
      if (caption != null && caption.isNotEmpty) 'message': caption,
      if (replyToId != null) 'replyToId': replyToId,
      if (durationMs != null) 'durationMs': durationMs,
    });
    final response = await _dio.post(
      '/api/v1/chat/messages/attachment',
      data: form,
      options: Options(contentType: 'multipart/form-data', sendTimeout: const Duration(minutes: 2), receiveTimeout: const Duration(minutes: 2)),
    );
    return ChatMessageDto.fromJson(Map<String, dynamic>.from(response.data as Map));
  }

  Future<List<int>> downloadAttachment(String messageId) async {
    final response = await _dio.get<List<int>>(
      '/api/v1/chat/messages/$messageId/attachment',
      options: Options(responseType: ResponseType.bytes, receiveTimeout: const Duration(minutes: 2)),
    );
    return response.data ?? const <int>[];
  }

  /// Tapping the same emoji again removes the reaction.
  Future<ChatMessageDto> react(String messageId, String emoji) async {
    final response = await _dio.post('/api/v1/chat/messages/$messageId/react', data: {'emoji': emoji});
    return ChatMessageDto.fromJson(Map<String, dynamic>.from(response.data as Map));
  }

  Future<void> typing(String toUserId) async {
    await _dio.post('/api/v1/chat/typing', queryParameters: {'toUserId': toUserId});
  }

  Future<bool> isTyping(String otherUserId) async {
    final response = await _dio.get('/api/v1/chat/typing', queryParameters: {'otherUserId': otherUserId});
    final data = response.data;
    return data is Map && data['typing'] == true;
  }

  Future<List<ChatUserDto>> getUsers() async {
    final response = await _dio.get('/api/v1/chat/users');
    final data = response.data;
    if (data is List) {
      return data.map((e) => ChatUserDto.fromJson(e as Map<String, dynamic>)).toList();
    }
    return [];
  }

  Future<void> ping() async {
    await _dio.post('/api/v1/chat/ping');
  }

  Future<void> goOffline() async {
    await _dio.post('/api/v1/chat/offline');
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

  /// Scans the front (required) and back (optional) of a card in one request. The server reads each side,
  /// merges them and lists any field the two sides disagree on in `conflicts`.
  Future<ExtractedLeadCardDto> scanCardSides({
    String? frontPath,
    List<int>? frontBytes,
    String frontName = 'card_front.jpg',
    String? backPath,
    List<int>? backBytes,
    String backName = 'card_back.jpg',
  }) async {
    Future<MultipartFile> part(String? path, List<int>? bytes, String name) async {
      if (path != null && path.isNotEmpty) return MultipartFile.fromFile(path, filename: name);
      return MultipartFile.fromBytes(bytes ?? const <int>[], filename: name);
    }

    final formData = FormData();
    formData.files.add(MapEntry('front', await part(frontPath, frontBytes, frontName)));
    if ((backPath != null && backPath.isNotEmpty) || (backBytes != null && backBytes.isNotEmpty)) {
      formData.files.add(MapEntry('back', await part(backPath, backBytes, backName)));
    }

    final response = await _dio.post(
      '/api/v1/leads/scan-card-file',
      data: formData,
      options: Options(contentType: 'multipart/form-data'),
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

