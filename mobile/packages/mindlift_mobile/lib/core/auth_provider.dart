import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:mindlift_api/mindlift_api.dart';

class SecureTokenStorage implements TokenStorage {
  const SecureTokenStorage(this._storage);
  final FlutterSecureStorage _storage;

  static const _keyAccess = 'mindlift_access_token';
  static const _keyRefresh = 'mindlift_refresh_token';

  @override
  Future<String?> getAccessToken() => _storage.read(key: _keyAccess);

  @override
  Future<String?> getRefreshToken() => _storage.read(key: _keyRefresh);

  @override
  Future<void> saveTokens({required String accessToken, required String refreshToken}) async {
    await _storage.write(key: _keyAccess, value: accessToken);
    await _storage.write(key: _keyRefresh, value: refreshToken);
  }

  @override
  Future<void> clearTokens() async {
    await _storage.delete(key: _keyAccess);
    await _storage.delete(key: _keyRefresh);
  }
}

class AuthState {
  const AuthState({
    this.isAuthenticated = false,
    this.isLoading = false,
    this.userFullName,
    this.userEmail,
    this.userId,
    this.userRole,
    this.userJobTitle,
    this.errorMessage,
  });

  final bool isAuthenticated;
  final bool isLoading;
  final String? userFullName;
  final String? userEmail;
  final String? userId;
  final String? userRole;
  final String? userJobTitle;
  final String? errorMessage;

  AuthState copyWith({
    bool? isAuthenticated,
    bool? isLoading,
    String? userFullName,
    String? userEmail,
    String? userId,
    String? userRole,
    String? userJobTitle,
    String? errorMessage,
  }) {
    return AuthState(
      isAuthenticated: isAuthenticated ?? this.isAuthenticated,
      isLoading: isLoading ?? this.isLoading,
      userFullName: userFullName ?? this.userFullName,
      userEmail: userEmail ?? this.userEmail,
      userId: userId ?? this.userId,
      userRole: userRole ?? this.userRole,
      userJobTitle: userJobTitle ?? this.userJobTitle,
      errorMessage: errorMessage,
    );
  }
}

class AuthNotifier extends StateNotifier<AuthState> {
  AuthNotifier({
    required this.tokenStorage,
    required this.authService,
    required this.onPurgeLocalData,
  }) : super(const AuthState(isLoading: true)) {
    _checkInitialAuth();
  }

  final TokenStorage tokenStorage;
  final AuthService authService;
  final Future<void> Function() onPurgeLocalData;

  Future<void> _checkInitialAuth() async {
    final token = await tokenStorage.getAccessToken();
    if (token != null && token.isNotEmpty) {
      state = state.copyWith(
        isAuthenticated: true,
        isLoading: false,
      );
      await _loadProfile();
    } else {
      state = state.copyWith(isAuthenticated: false, isLoading: false);
    }
  }

  /// Loads the real name, role and designation from the server (same source as the web portal).
  Future<void> _loadProfile() async {
    try {
      final me = await authService.getMe();
      final roleName = (me['roleName'] as String?)?.trim();
      final isOwner = me['isTenantOwner'] == true;
      final isAdmin = me['isAdmin'] == true;
      final role = (roleName != null && roleName.isNotEmpty)
          ? roleName
          : (isOwner ? 'Owner' : (isAdmin ? 'Admin' : 'Team member'));
      state = state.copyWith(
        userFullName: (me['fullName'] as String?)?.trim(),
        userEmail: me['email'] as String?,
        userId: (me['userId'] ?? me['id']) as String?,
        userRole: role,
        userJobTitle: (me['jobTitle'] as String?)?.trim(),
      );
    } catch (_) {}
  }

  Future<bool> login(String email, String password) async {
    state = state.copyWith(isLoading: true, errorMessage: null);
    try {
      await authService.login(
        LoginRequest(email: email, password: password),
      );
      state = state.copyWith(
        isAuthenticated: true,
        isLoading: false,
        userEmail: email,
      );
      await _loadProfile();
      return true;
    } catch (e) {
      String msg = 'Invalid credentials or connection error. Please try again.';
      if (e is DioException) {
        if (e.response != null && e.response?.data is Map) {
          final data = e.response!.data as Map;
          msg = data['detail'] as String? ?? data['title'] as String? ?? data['message'] as String? ?? msg;
        } else {
          msg = connectionProblem(e) ?? msg;
        }
      }
      state = state.copyWith(
        isAuthenticated: false,
        isLoading: false,
        errorMessage: msg,
      );
      return false;
    }
  }

  Future<void> logout() async {
    state = state.copyWith(isLoading: true);
    try {
      await authService.logout();
    } catch (_) {}
    await tokenStorage.clearTokens();
    await onPurgeLocalData();
    state = const AuthState(isAuthenticated: false, isLoading: false);
  }

  void handleSessionExpired() {
    tokenStorage.clearTokens();
    onPurgeLocalData();
    state = const AuthState(
      isAuthenticated: false,
      isLoading: false,
      errorMessage: 'Your session has expired. Please sign in again.',
    );
  }
}

final secureStorageProvider = Provider<FlutterSecureStorage>((ref) {
  return const FlutterSecureStorage();
});

final tokenStorageProvider = Provider<TokenStorage>((ref) {
  return SecureTokenStorage(ref.watch(secureStorageProvider));
});



/// Plain-language reason a request could not reach the server, or null if it did reach it.
String? connectionProblem(Object e) {
  if (e is! DioException) return null;
  final text = '${e.error ?? ''} ${e.message ?? ''}'.toLowerCase();
  if (text.contains('certificate') || text.contains('handshake')) {
    return "Secure connection failed. Check that your phone's date and time are correct (turn on automatic date & time).";
  }
  if (text.contains('failed host lookup') || text.contains('network is unreachable') || text.contains('no address associated')) {
    return 'No internet connection. Turn on Wi-Fi or mobile data and try again.';
  }
  if (e.type == DioExceptionType.connectionTimeout || e.type == DioExceptionType.receiveTimeout || e.type == DioExceptionType.sendTimeout) {
    return 'The server is taking a while to respond (it may be waking up). Please try again in a few seconds.';
  }
  if (e.type == DioExceptionType.connectionError) {
    return 'Cannot reach the server. Check your internet connection and try again.';
  }
  return null;
}
