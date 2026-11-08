import 'package:flutter/widgets.dart';
import 'colors.dart';

/// A callable TextStyle that can be used directly as a TextStyle or invoked as a function with optional color.
class MlTextStyle extends TextStyle {
  const MlTextStyle({
    super.fontSize,
    super.fontWeight,
    super.height,
    super.color,
  });

  TextStyle call({Color? color}) {
    if (color == null) return this;
    return copyWith(color: color);
  }
}

/// Typography tokens for MindLift UI.
/// Designed for field legibility, accessibility scaling (up to 200%), and bidirectional (LTR/RTL) rendering.
class MlTypography {
  const MlTypography._();

  static const String fontFamily = 'MindLiftSans';

  // Headings
  static const MlTextStyle headingLarge = MlTextStyle(
    fontSize: 24,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle headingMedium = MlTextStyle(
    fontSize: 20,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle headingSmall = MlTextStyle(
    fontSize: 18,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  // Titles
  static const MlTextStyle titleLarge = MlTextStyle(
    fontSize: 22,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle titleMedium = MlTextStyle(
    fontSize: 18,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle titleSmall = MlTextStyle(
    fontSize: 16,
    fontWeight: FontWeight.w600,
    height: 1.35,
    color: MlColors.textPrimary,
  );

  // Bodies
  static const MlTextStyle bodyLarge = MlTextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle bodyMedium = MlTextStyle(
    fontSize: 14,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textSecondary,
  );

  static const MlTextStyle bodySmall = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textMuted,
  );

  // Labels
  static const MlTextStyle labelLarge = MlTextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle labelMedium = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle label = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static const MlTextStyle caption = MlTextStyle(
    fontSize: 12,
    fontWeight: FontWeight.w400,
    height: 1.3,
    color: MlColors.textMuted,
  );

  static const MlTextStyle button = MlTextStyle(
    fontSize: 14,
    fontWeight: FontWeight.w600,
    height: 1.2,
    color: MlColors.textPrimary,
  );
}
