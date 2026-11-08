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
    this.userRole,
    this.errorMessage,
  });

  final bool isAuthenticated;
  final bool isLoading;
  final String? userFullName;
  final String? userEmail;
  final String? userRole;
  final String? errorMessage;

  AuthState copyWith({
    bool? isAuthenticated,
    bool? isLoading,
    String? userFullName,
    String? userEmail,
    String? userRole,
    String? errorMessage,
  }) {
    return AuthState(
      isAuthenticated: isAuthenticated ?? this.isAuthenticated,
      isLoading: isLoading ?? this.isLoading,
      userFullName: userFullName ?? this.userFullName,
      userEmail: userEmail ?? this.userEmail,
      userRole: userRole ?? this.userRole,
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
        userFullName: 'Sales Representative',
        userRole: 'Staff',
      );
    } else {
      state = state.copyWith(isAuthenticated: false, isLoading: false);
    }
  }

  Future<bool> login(String email, String password) async {
    state = state.copyWith(isLoading: true, errorMessage: null);
    try {
      final response = await authService.login(
        LoginRequest(email: email, password: password),
      );
      state = state.copyWith(
        isAuthenticated: true,
        isLoading: false,
        userFullName: response.fullName ?? 'Sales Staff',
        userEmail: response.email ?? email,
        userRole: response.role ?? 'Staff',
      );
      return true;
    } catch (e) {
      String msg = 'Invalid credentials or connection error. Please try again.';
      if (e is DioException) {
        if (e.response != null && e.response?.data is Map) {
          final data = e.response!.data as Map;
          msg = data['detail'] as String? ?? data['title'] as String? ?? data['message'] as String? ?? msg;
        } else if (e.type == DioExceptionType.connectionTimeout || e.type == DioExceptionType.connectionError) {
          msg = 'Cannot connect to server. Please verify the Server URL.';
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

