import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'network_provider.dart';

/// Tells the server this user is online only while the app is in the foreground and signed in.
class PresenceService with WidgetsBindingObserver {
  PresenceService(this._ref) {
    WidgetsBinding.instance.addObserver(this);
    _ref.listen(authNotifierProvider.select((s) => s.isAuthenticated), (prev, signedIn) {
      if (signedIn) {
        _start();
      } else {
        _stop(notify: prev == true);
      }
    }, fireImmediately: true);
    _ref.onDispose(() {
      WidgetsBinding.instance.removeObserver(this);
      _timer?.cancel();
    });
  }

  final Ref _ref;
  Timer? _timer;

  void _start() {
    _timer?.cancel();
    _ping();
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => _ping());
  }

  void _stop({bool notify = false}) {
    _timer?.cancel();
    _timer = null;
    if (notify) {
      _ref.read(chatServiceProvider).goOffline().catchError((_) {});
    }
  }

  void _ping() {
    _ref.read(chatServiceProvider).ping().catchError((_) {});
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (!_ref.read(authNotifierProvider).isAuthenticated) return;
    if (state == AppLifecycleState.resumed) {
      _start();
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.detached) {
      _stop(notify: true);
    }
  }
}

final presenceServiceProvider = Provider<PresenceService>((ref) => PresenceService(ref));
