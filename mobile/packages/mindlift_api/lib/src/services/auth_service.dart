import 'package:dio/dio.dart';
import '../client/api_client.dart';
import '../models/auth_models.dart';

class AuthService {
  const AuthService(this._client);
  final MindLiftApiClient _client;

  Dio get _dio => _client.rawDio;

  Future<TokenResponse> login(LoginRequest request) async {
    final response = await _dio.post(
      '/api/v1/auth/login',
      data: request.toJson(),
    );
    final tokenResponse = TokenResponse.fromJson(response.data as Map<String, dynamic>);
    if (tokenResponse.accessToken.isNotEmpty) {
      await _client.tokenStorage.saveTokens(
        accessToken: tokenResponse.accessToken,
        refreshToken: tokenResponse.refreshToken ?? '',
      );
    }
    return tokenResponse;
  }

  /// The signed-in user's profile: name, role and designation (same data as the web portal).
  Future<Map<String, dynamic>> getMe() async {
    final response = await _dio.get('/api/v1/auth/me');
    return Map<String, dynamic>.from(response.data as Map);
  }

  Future<void> logout() async {
    try {
      await _dio.post('/api/v1/auth/logout');
    } catch (_) {
      // Best-effort logout on server
    } finally {
      await _client.tokenStorage.clearTokens();
    }
  }

  Future<bool> isAuthenticated() async {
    final token = await _client.tokenStorage.getAccessToken();
    return token != null && token.isNotEmpty;
  }
}

