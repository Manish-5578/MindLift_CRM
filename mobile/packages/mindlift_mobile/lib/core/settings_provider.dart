import 'dart:ui' show Brightness;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

enum AppThemeMode { system, light, dark }

class AppSettings {
  const AppSettings({this.themeMode = AppThemeMode.system, this.biometricLock = false, this.loaded = false});
  final AppThemeMode themeMode;
  final bool biometricLock;
  final bool loaded;

  bool isDark(Brightness platform) =>
      themeMode == AppThemeMode.dark || (themeMode == AppThemeMode.system && platform == Brightness.dark);

  AppSettings copyWith({AppThemeMode? themeMode, bool? biometricLock, bool? loaded}) => AppSettings(
        themeMode: themeMode ?? this.themeMode,
        biometricLock: biometricLock ?? this.biometricLock,
        loaded: loaded ?? this.loaded,
      );
}

class SettingsNotifier extends StateNotifier<AppSettings> {
  SettingsNotifier() : super(const AppSettings()) {
    _load();
  }

  static const _storage = FlutterSecureStorage();
  static const _kTheme = 'mindlift_theme_mode';
  static const _kBio = 'mindlift_biometric_lock';
  final LocalAuthentication auth = LocalAuthentication();

  Future<void> _load() async {
    try {
      final t = await _storage.read(key: _kTheme);
      final b = await _storage.read(key: _kBio);
      state = AppSettings(
        themeMode: AppThemeMode.values.firstWhere((m) => m.name == t, orElse: () => AppThemeMode.system),
        biometricLock: b == '1',
        loaded: true,
      );
    } catch (_) {
      state = state.copyWith(loaded: true);
    }
  }

  Future<void> setThemeMode(AppThemeMode mode) async {
    state = state.copyWith(themeMode: mode);
    try {
      await _storage.write(key: _kTheme, value: mode.name);
    } catch (_) {}
  }

  /// True when the device has biometrics or a screen lock enrolled that we can prompt with.
  Future<bool> deviceSupportsAuth() async {
    try {
      return await auth.isDeviceSupported() || await auth.canCheckBiometrics;
    } catch (_) {
      return false;
    }
  }

  Future<bool> authenticate(String reason) async {
    try {
      return await auth.authenticate(
        localizedReason: reason,
        options: const AuthenticationOptions(stickyAuth: true, biometricOnly: false),
      );
    } catch (_) {
      return false;
    }
  }

  /// Enabling requires a successful prompt so we know the device's biometrics actually work.
  Future<bool> setBiometricLock(bool enabled) async {
    if (enabled) {
      if (!await deviceSupportsAuth()) return false;
      if (!await authenticate('Confirm to enable biometric unlock')) return false;
    }
    state = state.copyWith(biometricLock: enabled);
    try {
      await _storage.write(key: _kBio, value: enabled ? '1' : '0');
    } catch (_) {}
    return true;
  }
}

final settingsProvider = StateNotifierProvider<SettingsNotifier, AppSettings>((ref) => SettingsNotifier());
