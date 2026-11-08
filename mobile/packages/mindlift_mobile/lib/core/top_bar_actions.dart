import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'network_provider.dart';
import 'settings_provider.dart';

/// Appearance shortcut for the top-right corner: tap to step through Auto, Light and Dark.
class ThemeIconButton extends ConsumerWidget {
  const ThemeIconButton({super.key, this.color});
  final Color? color;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final mode = ref.watch(settingsProvider).themeMode;
    final (glyph, name) = switch (mode) {
      AppThemeMode.system => ('◐', 'Auto'),
      AppThemeMode.light => ('☀', 'Light'),
      AppThemeMode.dark => ('☾', 'Dark'),
    };
    return MlIconButton(
      label: 'Appearance: $name',
      icon: Text(glyph, style: TextStyle(fontSize: 19, color: color ?? MlColors.textPrimary, decoration: TextDecoration.none)),
      onPressed: () {
        HapticFeedback.selectionClick();
        final next = AppThemeMode.values[(mode.index + 1) % AppThemeMode.values.length];
        ref.read(settingsProvider.notifier).setThemeMode(next);
      },
    );
  }
}

/// Notifications bell with the unread count, for the top-right corner.
class BellButton extends ConsumerWidget {
  const BellButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(unreadNotificationCountProvider).value ?? 0;
    return Stack(
      clipBehavior: Clip.none,
      children: [
        MlIconButton(
          label: 'Notifications',
          icon: const Text('🔔', style: TextStyle(fontSize: 18)),
          onPressed: () => context.push('/notifications'),
        ),
        if (unread > 0)
          Positioned(
            top: 6,
            right: 6,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
              decoration: BoxDecoration(color: MlColors.danger, borderRadius: BorderRadius.circular(MlRadii.full)),
              constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
              alignment: Alignment.center,
              child: Text(
                unread > 99 ? '99+' : '$unread',
                style: const TextStyle(fontSize: 9, fontWeight: FontWeight.w700, color: MlColors.white, decoration: TextDecoration.none),
              ),
            ),
          ),
      ],
    );
  }
}

/// The two icons every main screen shows in its top-right corner.
List<Widget> topBarActions() => const [ThemeIconButton(), BellButton()];
