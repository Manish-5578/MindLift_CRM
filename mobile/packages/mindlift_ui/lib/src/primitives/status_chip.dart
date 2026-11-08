import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

enum MlStatusState { primary, success, warning, danger, neutral }

/// Semantic status chip indicator.
class MlStatusChip extends StatelessWidget {
  const MlStatusChip({
    super.key,
    required this.label,
    this.state,
    this.dotColor,
    this.backgroundColor,
    this.textColor,
    this.showDot = true,
  });

  final String label;
  final MlStatusState? state;
  final Color? dotColor;
  final Color? backgroundColor;
  final Color? textColor;
  final bool showDot;

  @override
  Widget build(BuildContext context) {
    Color effectiveDotColor = dotColor ?? MlColors.primary;
    Color effectiveBg = backgroundColor ?? MlColors.surfaceElevated;
    Color effectiveText = textColor ?? MlColors.textPrimary;

    if (state != null) {
      switch (state!) {
        case MlStatusState.primary:
          effectiveDotColor = MlColors.primaryLight;
          effectiveBg = MlColors.primary.withOpacity(0.18);
          effectiveText = MlColors.primaryLight;
          break;
        case MlStatusState.success:
          effectiveDotColor = MlColors.success;
          effectiveBg = MlColors.success.withOpacity(0.18);
          effectiveText = MlColors.success;
          break;
        case MlStatusState.warning:
          effectiveDotColor = MlColors.warning;
          effectiveBg = MlColors.warning.withOpacity(0.18);
          effectiveText = MlColors.warning;
          break;
        case MlStatusState.danger:
          effectiveDotColor = MlColors.danger;
          effectiveBg = MlColors.danger.withOpacity(0.18);
          effectiveText = MlColors.danger;
          break;
        case MlStatusState.neutral:
          effectiveDotColor = MlColors.textMuted;
          effectiveBg = MlColors.surfaceElevated;
          effectiveText = MlColors.textMuted;
          break;
      }
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: effectiveBg,
        borderRadius: MlRadii.borderFull,
        border: Border.all(color: MlColors.border, width: 1),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (showDot) ...[
            Container(
              width: 6,
              height: 6,
              decoration: BoxDecoration(
                color: effectiveDotColor,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: MlSpacing.xxs),
          ],
          Text(
            label,
            style: MlTypography.caption.copyWith(
              color: effectiveText,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
