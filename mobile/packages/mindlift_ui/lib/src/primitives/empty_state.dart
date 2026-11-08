import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'button.dart';

/// Reusable empty state primitive for lists, search, filters, and offline views.
class MlEmptyState extends StatelessWidget {
  const MlEmptyState({
    super.key,
    required this.title,
    this.subtitle,
    this.icon,
    this.actionLabel,
    this.onAction,
  });

  final String title;
  final String? subtitle;
  final Widget? icon;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.xl, vertical: MlSpacing.xxl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            if (icon != null) ...[
              Container(
                width: 64,
                height: 64,
                decoration: BoxDecoration(
                  color: MlColors.surface,
                  shape: BoxShape.circle,
                  border: Border.all(color: MlColors.border),
                ),
                alignment: Alignment.center,
                child: icon,
              ),
              const SizedBox(height: MlSpacing.lg),
            ],
            Text(
              title,
              style: MlTypography.headingSmall.copyWith(color: MlColors.textPrimary),
              textAlign: TextAlign.center,
            ),
            if (subtitle != null) ...[
              const SizedBox(height: MlSpacing.xs),
              Text(
                subtitle!,
                style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
                textAlign: TextAlign.center,
              ),
            ],
            if (actionLabel != null && onAction != null) ...[
              const SizedBox(height: MlSpacing.lg),
              MlButton(
                label: actionLabel!,
                onPressed: onAction,
                variant: MlButtonVariant.secondary,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

