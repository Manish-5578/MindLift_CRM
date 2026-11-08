import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'core/app_lock.dart';
import 'core/locale_provider.dart';
import 'core/push_service.dart';
import 'core/settings_provider.dart';
import 'dart:ui' show PlatformDispatcher;
import 'core/router.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    const ProviderScope(
      child: MindLiftMobileApp(),
    ),
  );
}

class MindLiftMobileApp extends ConsumerStatefulWidget {
  const MindLiftMobileApp({super.key});

  @override
  ConsumerState<MindLiftMobileApp> createState() => _MindLiftMobileAppState();
}

class _MindLiftMobileAppState extends ConsumerState<MindLiftMobileApp> with WidgetsBindingObserver {
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
  void didChangePlatformBrightness() => setState(() {});

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(settingsProvider);
    final dark = settings.isDark(PlatformDispatcher.instance.platformBrightness);
    MlColors.isDark = dark;
    final language = ref.watch(localeProvider);
    final router = ref.watch(routerProvider);
    ref.watch(pushServiceProvider);

    return AppLockGate(
      child: KeyedSubtree(
        key: ValueKey(dark),
        child: MlTheme(
          isDark: dark,
          child: Directionality(
            textDirection: language.direction,
            child: WidgetsApp.router(
              title: 'MindLift CRM',
              color: MlColors.primary,
              routerConfig: router,
              locale: language.locale,
              supportedLocales: const [
                Locale('en'),
                Locale('ar'),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
