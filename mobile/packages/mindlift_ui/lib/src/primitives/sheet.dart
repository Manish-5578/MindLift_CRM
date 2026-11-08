import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'button.dart';
import 'icons.dart';

/// Modal bottom sheet primitive replacing platform sheets.
class MlSheet extends StatelessWidget {
  const MlSheet({
    super.key,
    required this.child,
    this.title,
    this.onClose,
  });

  final Widget child;
  final String? title;
  final VoidCallback? onClose;

  static Future<T?> show<T>({
    required BuildContext context,
    required WidgetBuilder builder,
    String? title,
  }) {
    return showGeneralDialog<T>(
      context: context,
      barrierDismissible: true,
      barrierLabel: 'Dismiss',
      barrierColor: MlColors.overlay,
      transitionDuration: const Duration(milliseconds: 320),
      pageBuilder: (ctx, anim1, anim2) => Align(
        alignment: Alignment.bottomCenter,
        child: MlSheet(
          title: title,
          onClose: () => Navigator.of(ctx).pop(),
          child: builder(ctx),
        ),
      ),
      transitionBuilder: (ctx, anim1, anim2, child) {
        // Material "emphasized" motion: decelerate in, accelerate out.
        final curved = CurvedAnimation(parent: anim1, curve: const Cubic(0.05, 0.7, 0.1, 1.0), reverseCurve: const Cubic(0.3, 0.0, 0.8, 0.15));
        return SlideTransition(
          position: Tween<Offset>(begin: const Offset(0, 1), end: Offset.zero).animate(curved),
          child: child,
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      constraints: BoxConstraints(
        maxHeight: MediaQuery.sizeOf(context).height * 0.85,
      ),
      decoration: BoxDecoration(
        color: MlColors.surface,
        borderRadius: const BorderRadius.only(
          topLeft: Radius.circular(28),
          topRight: Radius.circular(28),
        ),
        border: Border(top: BorderSide(color: MlColors.border, width: 1)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                margin: const EdgeInsets.only(top: 8, bottom: 6),
                width: 36,
                height: 4,
                decoration: BoxDecoration(
                  color: MlColors.border,
                  borderRadius: MlRadii.borderFull,
                ),
              ),
            ),
            if (title != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(MlSpacing.md, 4, MlSpacing.md, MlSpacing.sm),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(title!, style: MlTypography.titleMedium()),
                    ),
                    MlIconButton(
                      label: 'Close',
                      icon: MlIcon(MlIcons.close, size: 22, color: MlColors.textMuted),
                      onPressed: onClose ?? () => Navigator.of(context).pop(),
                    ),
                  ],
                ),
              ),
            Flexible(
              child: SingleChildScrollView(
                keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
                child: child,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

