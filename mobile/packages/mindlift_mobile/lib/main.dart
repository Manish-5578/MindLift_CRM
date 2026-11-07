import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'core/locale_provider.dart';
import 'core/router.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    const ProviderScope(
      child: MindLiftMobileApp(),
    ),
  );
}

class MindLiftMobileApp extends ConsumerWidget {
  const MindLiftMobileApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final language = ref.watch(localeProvider);
    final router = ref.watch(routerProvider);

    return MlTheme(
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
    );
  }
}
