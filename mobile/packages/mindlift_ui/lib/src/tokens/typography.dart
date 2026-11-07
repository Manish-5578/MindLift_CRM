import 'package:flutter/widgets.dart';
import 'colors.dart';

/// Typography tokens for MindLift UI.
/// Designed for field legibility, accessibility scaling (up to 200%), and bidirectional (LTR/RTL) rendering.
class MlTypography {
  const MlTypography._();

  static const String fontFamily = 'MindLiftSans';

  static TextStyle titleLarge({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 22,
        fontWeight: FontWeight.w700,
        height: 1.25,
        color: color,
      );

  static TextStyle titleMedium({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 18,
        fontWeight: FontWeight.w600,
        height: 1.3,
        color: color,
      );

  static TextStyle titleSmall({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 16,
        fontWeight: FontWeight.w600,
        height: 1.35,
        color: color,
      );

  static TextStyle bodyLarge({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 15,
        fontWeight: FontWeight.w400,
        height: 1.4,
        color: color,
      );

  static TextStyle bodyMedium({Color color = MlColors.textSecondary}) => TextStyle(
        fontSize: 14,
        fontWeight: FontWeight.w400,
        height: 1.4,
        color: color,
      );

  static TextStyle label({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 13,
        fontWeight: FontWeight.w600,
        height: 1.3,
        color: color,
      );

  static TextStyle caption({Color color = MlColors.textMuted}) => TextStyle(
        fontSize: 12,
        fontWeight: FontWeight.w400,
        height: 1.3,
        color: color,
      );

  static TextStyle button({Color color = MlColors.textPrimary}) => TextStyle(
        fontSize: 14,
        fontWeight: FontWeight.w600,
        height: 1.2,
        color: color,
      );
}
