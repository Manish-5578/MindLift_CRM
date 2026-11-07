import 'dart:async';
import 'package:dio/dio.dart';
import '../storage/token_storage.dart';
import '../models/auth_models.dart';

typedef SessionExpiredCallback = void Function();

class AuthInterceptor extends QueuedInterceptor {
  AuthInterceptor({
    required this.tokenStorage,
    required this.dio,
    this.onSessionExpired,
  });

  final TokenStorage tokenStorage;
  final Dio dio;
  final SessionExpiredCallback? onSessionExpired;

  bool _isRefreshing = false;
  Completer<String?>? _refreshCompleter;

  @override
  Future<void> onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    // Add mobile client header
    options.headers['X-Client'] = 'mobile';

    // Don't attach token to auth endpoints
    if (options.path.contains('/auth/login') || options.path.contains('/auth/refresh')) {
      return handler.next(options);
    }

    final token = await tokenStorage.getAccessToken();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    return handler.next(options);
  }

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    // Check if 401 Unauthorized
    if (err.response?.statusCode == 401 && !err.requestOptions.path.contains('/auth/login')) {
      if (err.requestOptions.path.contains('/auth/refresh')) {
        // Refresh token itself expired or revoked
        await tokenStorage.clearTokens();
        onSessionExpired?.call();
        return handler.next(err);
      }

      try {
        final newAccessToken = await _performTokenRefresh();
        if (newAccessToken != null) {
          // Retry the failed request with the new access token
          final requestOptions = err.requestOptions;
          requestOptions.headers['Authorization'] = 'Bearer $newAccessToken';
          final response = await dio.fetch(requestOptions);
          return handler.resolve(response);
        } else {
          onSessionExpired?.call();
          return handler.next(err);
        }
      } catch (e) {
        onSessionExpired?.call();
        return handler.next(err);
      }
    }

    return handler.next(err);
  }

  Future<String?> _performTokenRefresh() async {
    if (_isRefreshing) {
      return _refreshCompleter?.future;
    }

    _isRefreshing = true;
    _refreshCompleter = Completer<String?>();

    try {
      final currentRefreshToken = await tokenStorage.getRefreshToken();
      if (currentRefreshToken == null || currentRefreshToken.isEmpty) {
        _refreshCompleter?.complete(null);
        return null;
      }

      // Create an unintercepted Dio instance for refresh call to prevent recursion
      final refreshDio = Dio(
        BaseOptions(
          baseUrl: dio.options.baseUrl,
          headers: {'X-Client': 'mobile'},
        ),
      );

      final response = await refreshDio.post(
        '/api/v1/auth/refresh',
        data: RefreshTokenRequest(refreshToken: currentRefreshToken).toJson(),
      );

      if (response.statusCode == 200 && response.data != null) {
        final tokenData = TokenResponse.fromJson(response.data as Map<String, dynamic>);
        await tokenStorage.saveTokens(
          accessToken: tokenData.accessToken,
          refreshToken: tokenData.refreshToken ?? currentRefreshToken,
        );
        _refreshCompleter?.complete(tokenData.accessToken);
        return tokenData.accessToken;
      } else {
        await tokenStorage.clearTokens();
        _refreshCompleter?.complete(null);
        return null;
      }
    } catch (e) {
      await tokenStorage.clearTokens();
      _refreshCompleter?.complete(null);
      return null;
    } finally {
      _isRefreshing = false;
      _refreshCompleter = null;
    }
  }
}
