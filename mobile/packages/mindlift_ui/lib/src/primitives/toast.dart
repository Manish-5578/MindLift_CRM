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
    Color? backgroundColor,
    Duration duration = const Duration(seconds: 3),
  }) {
    final overlay = Overlay.maybeOf(context);
    if (overlay == null) return;

    late OverlayEntry entry;
    entry = OverlayEntry(
      builder: (ctx) => _ToastWidget(
        message: message,
        type: type,
        customBackgroundColor: backgroundColor,
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
    this.customBackgroundColor,
    required this.onDismiss,
  });

  final String message;
  final MlToastType type;
  final Color? customBackgroundColor;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    Color bg = customBackgroundColor ?? const Color(0xFF1E293B);
    Color border = MlColors.border;

    if (customBackgroundColor == null) {
      switch (type) {
        case MlToastType.success:
          bg = const Color(0xFF064E3B);
          border = MlColors.success;
          break;
        case MlToastType.error:
          bg = const Color(0xFF7F1D1D);
          border = MlColors.danger;
          break;
        case MlToastType.info:
          bg = MlColors.surface;
          border = MlColors.primary;
          break;
      }
    }

    return Positioned(
      bottom: 48,
      left: 20,
      right: 20,
      child: Center(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: MlSpacing.sm),
          decoration: BoxDecoration(
            color: bg,
            borderRadius: MlRadii.borderFull,
            border: Border.all(color: border, width: 1),
            boxShadow: const [
              BoxShadow(
                color: Color(0x66000000),
                blurRadius: 12,
                offset: Offset(0, 4),
              ),
            ],
          ),
          child: Text(
            message,
            style: MlTypography.caption.copyWith(color: MlColors.white),
            textAlign: TextAlign.center,
          ),
        ),
      ),
    );
  }
}
