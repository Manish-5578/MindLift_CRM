import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

enum MlToastType { success, error, info }

/// Transient notification toast replacing SnackBar.
class MlToast {
  const MlToast._();

  static void show(
    BuildContext context, {
    required String message,
    MlToastType type = MlToastType.info,
    Duration duration = const Duration(seconds: 3),
  }) {
    final overlay = Overlay.maybeOf(context);
    if (overlay == null) return;

    late OverlayEntry entry;
    entry = OverlayEntry(
      builder: (ctx) => _ToastWidget(
        message: message,
        type: type,
        onDismiss: () => entry.remove(),
      ),
    );

    overlay.insert(entry);
    Future.delayed(duration, () {
      if (entry.mounted) entry.remove();
    });
  }
}

class _ToastWidget extends StatelessWidget {
  const _ToastWidget({
    required this.message,
    required this.type,
    required this.onDismiss,
  });

  final String message;
  final MlToastType type;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final (bg, border, text) = switch (type) {
      MlToastType.success => (const Color(0xFF064E3B), MlColors.success, const Color(0xFFD1FAE5)),
      MlToastType.error => (const Color(0xFF7F1D1D), MlColors.danger, const Color(0xFFFEE2E2)),
      MlToastType.info => (MlColors.surfaceElevated, MlColors.info, MlColors.textPrimary),
    };

    return SafeArea(
      child: Align(
        alignment: Alignment.topCenter,
        child: Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 10),
            decoration: BoxDecoration(
              color: bg,
              borderRadius: MlRadii.borderMd,
              border: Border.all(color: border, width: 1.5),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Flexible(
                  child: Text(
                    message,
                    style: MlTypography.caption(color: text),
                  ),
                ),
                const SizedBox(width: MlSpacing.xs),
                GestureDetector(
                  onTap: onDismiss,
                  child: Text('✕', style: TextStyle(color: text, fontSize: 12)),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
