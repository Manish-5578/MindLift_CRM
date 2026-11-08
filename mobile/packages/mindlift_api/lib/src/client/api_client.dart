import 'package:dio/dio.dart';
import '../storage/token_storage.dart';
import 'auth_interceptor.dart';

class MindLiftApiClient {
  MindLiftApiClient({
    required this.baseUrl,
    required this.tokenStorage,
    this.onSessionExpired,
  }) {
    _dio = Dio(
      BaseOptions(
        baseUrl: baseUrl,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 30),
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'X-Client': 'mobile',
        },
      ),
    );

    _dio.interceptors.add(
      AuthInterceptor(
        tokenStorage: tokenStorage,
        dio: _dio,
        onSessionExpired: onSessionExpired,
      ),
    );
  }

  final String baseUrl;
  final TokenStorage tokenStorage;
  final SessionExpiredCallback? onSessionExpired;
  late final Dio _dio;

  Dio get rawDio => _dio;
}

