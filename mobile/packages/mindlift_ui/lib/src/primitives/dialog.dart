import 'package:flutter/widgets.dart';
import '../tokens/strings.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'button.dart';

/// Bespoke modal dialog primitive replacing AlertDialog.
class MlDialog extends StatelessWidget {
  const MlDialog({
    super.key,
    required this.title,
    required this.message,
    String? confirmText,
    String? confirmLabel,
    this.cancelText = 'Cancel',
    this.onConfirm,
    this.onCancel,
    bool? isDanger,
    bool? isDestructive,
  })  : confirmText = confirmText ?? confirmLabel ?? 'Confirm',
        isDanger = isDanger ?? isDestructive ?? false;

  final String title;
  final String message;
  final String confirmText;
  final String cancelText;
  final VoidCallback? onConfirm;
  final VoidCallback? onCancel;
  final bool isDanger;

  static Future<bool?> show({
    required BuildContext context,
    required String title,
    required String message,
    String? confirmText,
    String? confirmLabel,
    String cancelText = 'Cancel',
    bool? isDanger,
    bool? isDestructive,
    VoidCallback? onConfirm,
    VoidCallback? onCancel,
  }) {
    return showGeneralDialog<bool>(
      context: context,
      barrierDismissible: true,
      barrierLabel: 'Dismiss',
      barrierColor: MlColors.overlay,
      transitionDuration: const Duration(milliseconds: 200),
      pageBuilder: (ctx, anim1, anim2) => Center(
        child: MlDialog(
          title: title,
          message: message,
          confirmText: confirmText ?? confirmLabel ?? 'Confirm',
          cancelText: cancelText,
          isDanger: isDanger ?? isDestructive ?? false,
          onConfirm: () {
            Navigator.of(ctx).pop(true);
            onConfirm?.call();
          },
          onCancel: () {
            Navigator.of(ctx).pop(false);
            onCancel?.call();
          },
        ),
      ),
      transitionBuilder: (ctx, anim1, anim2, child) {
        return FadeTransition(
          opacity: anim1,
          child: ScaleTransition(
            scale: Tween<double>(begin: 0.95, end: 1.0).animate(anim1),
            child: child,
          ),
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 320,
      margin: const EdgeInsets.symmetric(horizontal: MlSpacing.lg),
      padding: const EdgeInsets.all(MlSpacing.lg),
      decoration: BoxDecoration(
        color: MlColors.surface,
        borderRadius: MlRadii.borderLg,
        border: Border.all(color: MlColors.border, width: 1),
        boxShadow: const [
          BoxShadow(
            color: Color(0x1F0F172A),
            blurRadius: 24,
            offset: Offset(0, 8),
          ),
        ],
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            MlStrings.t(title),
            style: MlTypography.headingSmall,
          ),
          const SizedBox(height: MlSpacing.xs),
          Text(
            MlStrings.t(message),
            style: MlTypography.bodySmall,
          ),
          const SizedBox(height: MlSpacing.lg),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              MlButton(
                text: cancelText,
                variant: MlButtonVariant.ghost,
                size: MlButtonSize.small,
                onTap: onCancel,
              ),
              const SizedBox(width: MlSpacing.xs),
              MlButton(
                text: confirmText,
                variant: isDanger ? MlButtonVariant.danger : MlButtonVariant.primary,
                size: MlButtonSize.small,
                onTap: onConfirm,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
