import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

/// Semantic status chip indicator.
class MlStatusChip extends StatelessWidget {
  const MlStatusChip({
    super.key,
    required this.label,
    this.dotColor,
    this.backgroundColor,
    this.textColor,
    this.showDot = true,
  });

  final String label;
  final Color? dotColor;
  final Color? backgroundColor;
  final Color? textColor;
  final bool showDot;

  @override
  Widget build(BuildContext context) {
    final effectiveDotColor = dotColor ?? MlColors.primary;
    final effectiveBg = backgroundColor ?? MlColors.surfaceElevated;
    final effectiveText = textColor ?? MlColors.textPrimary;

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
            style: MlTypography.caption(color: effectiveText).copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
