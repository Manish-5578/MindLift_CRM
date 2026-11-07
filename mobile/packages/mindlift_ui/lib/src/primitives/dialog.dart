import 'package:flutter/widgets.dart';
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
    this.confirmText = 'Confirm',
    this.cancelText = 'Cancel',
    this.onConfirm,
    this.onCancel,
    this.isDanger = false,
  });

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
    String confirmText = 'Confirm',
    String cancelText = 'Cancel',
    bool isDanger = false,
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
          confirmText: confirmText,
          cancelText: cancelText,
          isDanger: isDanger,
          onConfirm: () => Navigator.of(ctx).pop(true),
          onCancel: () => Navigator.of(ctx).pop(false),
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
      margin: const EdgeInsets.all(MlSpacing.lg),
      padding: const EdgeInsets.all(MlSpacing.xl),
      decoration: BoxDecoration(
        color: MlColors.surface,
        borderRadius: MlRadii.borderLg,
        border: Border.all(color: MlColors.border, width: 1.5),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title, style: MlTypography.titleMedium()),
          const SizedBox(height: MlSpacing.xs),
          Text(message, style: MlTypography.bodyMedium()),
          const SizedBox(height: MlSpacing.xl),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              MlButton(
                text: cancelText,
                variant: MlButtonVariant.ghost,
                onTap: onCancel ?? () => Navigator.of(context).pop(false),
              ),
              const SizedBox(width: MlSpacing.xs),
              MlButton(
                text: confirmText,
                variant: isDanger ? MlButtonVariant.danger : MlButtonVariant.primary,
                onTap: onConfirm ?? () => Navigator.of(context).pop(true),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
