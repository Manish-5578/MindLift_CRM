import 'package:flutter/widgets.dart';
import '../tokens/strings.dart';
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
          effectiveDotColor = MlColors.primary;
          effectiveBg = MlColors.primarySoft;
          effectiveText = MlColors.primary;
          break;
        case MlStatusState.success:
          effectiveDotColor = MlColors.success;
          effectiveBg = MlColors.successSoft;
          effectiveText = const Color(0xFF065F46);
          break;
        case MlStatusState.warning:
          effectiveDotColor = MlColors.warning;
          effectiveBg = MlColors.warningSoft;
          effectiveText = const Color(0xFF92400E);
          break;
        case MlStatusState.danger:
          effectiveDotColor = MlColors.danger;
          effectiveBg = MlColors.dangerSoft;
          effectiveText = const Color(0xFF991B1B);
          break;
        case MlStatusState.neutral:
          effectiveDotColor = MlColors.textMuted;
          effectiveBg = MlColors.surfaceElevated;
          effectiveText = MlColors.textSecondary;
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
            MlStrings.t(label),
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
