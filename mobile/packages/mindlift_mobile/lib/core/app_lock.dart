import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'network_provider.dart';
import 'settings_provider.dart';

/// Covers the app with a lock screen until the device biometrics (or screen lock) succeed.
/// Locks on cold start and when the app returns from the background after [_grace].
class AppLockGate extends ConsumerStatefulWidget {
  const AppLockGate({super.key, required this.child});
  final Widget child;

  @override
  ConsumerState<AppLockGate> createState() => _AppLockGateState();
}

class _AppLockGateState extends ConsumerState<AppLockGate> with WidgetsBindingObserver {
  static const _grace = Duration(seconds: 20);
  bool _locked = true;
  bool _prompting = false;
  DateTime? _pausedAt;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      _pausedAt = DateTime.now();
    } else if (state == AppLifecycleState.resumed) {
      final p = _pausedAt;
      _pausedAt = null;
      if (p != null && DateTime.now().difference(p) > _grace && ref.read(settingsProvider).biometricLock) {
        setState(() => _locked = true);
        _prompt();
      }
    }
  }

  Future<void> _prompt() async {
    if (_prompting) return;
    _prompting = true;
    final ok = await ref.read(settingsProvider.notifier).authenticate('Unlock MindLift CRM');
    _prompting = false;
    if (ok && mounted) setState(() => _locked = false);
  }

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(settingsProvider);
    final signedIn = ref.watch(authNotifierProvider.select((s) => s.isAuthenticated));
    final wantsLock = settings.biometricLock && signedIn;
    final needLock = settings.loaded && wantsLock && _locked;
    if (settings.loaded && !wantsLock && _locked) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) setState(() => _locked = false);
      });
    }
    if (needLock && !_prompting) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _prompt());
    }
    return Directionality(
      textDirection: TextDirection.ltr,
      child: Stack(
        children: [
          widget.child,
          if (needLock) Positioned.fill(child: _LockScreen(onUnlock: _prompt)),
        ],
      ),
    );
  }
}

class _LockScreen extends StatelessWidget {
  const _LockScreen({required this.onUnlock});
  final VoidCallback onUnlock;

  @override
  Widget build(BuildContext context) {
    const white = Color(0xFFFFFFFF);
    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [MlColors.primary, const Color(0xFF1E1B4B)],
        ),
      ),
      child: SafeArea(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              width: 112,
              height: 112,
              decoration: BoxDecoration(
                color: const Color(0x26FFFFFF),
                shape: BoxShape.circle,
                border: Border.all(color: const Color(0x66FFFFFF), width: 2),
                boxShadow: const [BoxShadow(color: Color(0x40000000), blurRadius: 30, offset: Offset(0, 12))],
              ),
              alignment: Alignment.center,
              child: const Text('🔐', style: TextStyle(fontSize: 52, decoration: TextDecoration.none)),
            ),
            const SizedBox(height: 28),
            const Text('MindLift CRM',
                style: TextStyle(fontSize: 26, fontWeight: FontWeight.w700, color: white, decoration: TextDecoration.none)),
            const SizedBox(height: 8),
            const Text('Locked. Verify with your fingerprint, face or screen lock.',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 14, color: Color(0xCCFFFFFF), decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
            const SizedBox(height: 36),
            GestureDetector(
              onTap: onUnlock,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 14),
                decoration: BoxDecoration(color: white, borderRadius: BorderRadius.circular(MlRadii.full)),
                child: Text('Unlock',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: MlColors.primary, decoration: TextDecoration.none)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
