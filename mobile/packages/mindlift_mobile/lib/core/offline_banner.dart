import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'offline_store.dart';

/// Slim strip under the status bar that appears only when the phone is offline, syncing, or has changes that need attention.
class OfflineBanner extends ConsumerWidget {
  const OfflineBanner({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(outboxProvider);
    String? text;
    var color = const Color(0xFFB45309);
    IconData icon = MlIcons.warning;
    if (!s.online) {
      text = s.pending > 0
          ? 'You are offline. ${s.pending} change${s.pending == 1 ? '' : 's'} will sync when you are back online.'
          : 'You are offline. Showing saved data.';
    } else if (s.syncing) {
      text = 'Syncing your changes…';
      color = MlColors.primary;
      icon = MlIcons.refresh;
    } else if (s.failed.isNotEmpty) {
      text = '${s.failed.length} change${s.failed.length == 1 ? '' : 's'} could not be saved. Open Profile to review.';
      color = MlColors.danger;
    }
    return AnimatedSize(
      duration: const Duration(milliseconds: 240),
      curve: Curves.easeOutCubic,
      alignment: Alignment.topCenter,
      child: text == null
          ? const SizedBox(width: double.infinity)
          : Container(
              width: double.infinity,
              color: color,
              padding: EdgeInsets.fromLTRB(16, MediaQuery.paddingOf(context).top + 6, 16, 6),
              child: Row(
                children: [
                  Icon(icon, size: 16, color: MlColors.white),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(text,
                        style: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: MlColors.white, decoration: TextDecoration.none)),
                  ),
                ],
              ),
            ),
    );
  }
}
