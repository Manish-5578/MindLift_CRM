import 'dart:async';
import 'dart:math';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'network_provider.dart';
import 'router.dart';

/// Background/terminated-state handler. Messages carrying a `notification` block are shown by the OS itself.
@pragma('vm:entry-point')
Future<void> firebaseBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp();
}

final mobileDevicesServiceProvider = Provider<MobileDevicesService>((ref) {
  return MobileDevicesService(ref.watch(apiClientProvider));
});

/// Registers this device's FCM token with the server after sign-in and surfaces pushes.
class PushService {
  PushService(this._ref) {
    _init();
  }

  final Ref _ref;
  final _local = FlutterLocalNotificationsPlugin();
  static const _storage = FlutterSecureStorage();
  static const _kInstall = 'mindlift_installation_id';
  StreamSubscription<String>? _tokenSub;
  bool _ready = false;
  String? _installationId;

  static const _androidChannel = AndroidNotificationChannel(
    'mindlift_push',
    'MindLift alerts',
    description: 'Tasks, deals, activities and chat messages',
    importance: Importance.high,
  );

  Future<void> _init() async {
    try {
      await Firebase.initializeApp();
      FirebaseMessaging.onBackgroundMessage(firebaseBackgroundHandler);
      await _local.initialize(
        const InitializationSettings(android: AndroidInitializationSettings('@mipmap/ic_launcher')),
        onDidReceiveNotificationResponse: (r) => _go(r.payload),
      );
      await _local
          .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
          ?.createNotificationChannel(_androidChannel);
      await FirebaseMessaging.instance.requestPermission();
      FirebaseMessaging.onMessage.listen(_onForeground);
      FirebaseMessaging.onMessageOpenedApp.listen((m) => _go(m.data['route']));
      final initial = await FirebaseMessaging.instance.getInitialMessage();
      if (initial != null) _go(initial.data['route']);
      _ready = true;
    } catch (e) {
      debugPrint('Push init failed: $e');
    }
    _ref.listen(authNotifierProvider.select((s) => s.isAuthenticated), (_, signedIn) {
      if (signedIn) {
        _register();
      } else {
        _unregister();
      }
    }, fireImmediately: true);
    _ref.onDispose(() => _tokenSub?.cancel());
  }

  Future<String> _installation() async {
    if (_installationId != null) return _installationId!;
    var id = await _storage.read(key: _kInstall);
    if (id == null || id.isEmpty) {
      final r = Random.secure();
      id = List.generate(16, (_) => r.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
      await _storage.write(key: _kInstall, value: id);
    }
    return _installationId = id;
  }

  Future<void> _register() async {
    if (!_ready) return;
    try {
      final token = await FirebaseMessaging.instance.getToken();
      if (token == null) return;
      await _send(token);
      _tokenSub?.cancel();
      _tokenSub = FirebaseMessaging.instance.onTokenRefresh.listen((t) => _send(t));
    } catch (e) {
      debugPrint('Push register failed: $e');
    }
  }

  Future<void> _send(String token) async {
    await _ref.read(mobileDevicesServiceProvider).registerCurrentDevice(
          RegisterMobileDeviceRequest(
            installationId: await _installation(),
            platform: 'android',
            environment: 'production',
            deviceToken: token,
          ),
        );
  }

  Future<void> _unregister() async {
    _tokenSub?.cancel();
    // Revocation needs a valid session, so it is attempted best-effort before tokens are cleared elsewhere.
  }

  void _onForeground(RemoteMessage m) {
    final n = m.notification;
    if (n == null) return;
    _local.show(
      m.hashCode,
      n.title,
      n.body,
      NotificationDetails(
        android: AndroidNotificationDetails(_androidChannel.id, _androidChannel.name,
            channelDescription: _androidChannel.description, importance: Importance.high, priority: Priority.high),
      ),
      payload: m.data['route'],
    );
  }

  void _go(String? route) {
    if (route == null || route.isEmpty || !route.startsWith('/')) return;
    try {
      _ref.read(routerProvider).go(route);
    } catch (_) {}
  }
}

final pushServiceProvider = Provider<PushService>((ref) => PushService(ref));
