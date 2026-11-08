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

  static String fontFamily = 'MindLiftSans';

  // Headings
  static MlTextStyle headingLarge = MlTextStyle(
    fontSize: 24,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static MlTextStyle headingMedium = MlTextStyle(
    fontSize: 20,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static MlTextStyle headingSmall = MlTextStyle(
    fontSize: 18,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  // Titles
  static MlTextStyle titleLarge = MlTextStyle(
    fontSize: 22,
    fontWeight: FontWeight.w700,
    height: 1.25,
    color: MlColors.textPrimary,
  );

  static MlTextStyle titleMedium = MlTextStyle(
    fontSize: 18,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static MlTextStyle titleSmall = MlTextStyle(
    fontSize: 16,
    fontWeight: FontWeight.w600,
    height: 1.35,
    color: MlColors.textPrimary,
  );

  // Bodies
  static MlTextStyle bodyLarge = MlTextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textPrimary,
  );

  static MlTextStyle bodyMedium = MlTextStyle(
    fontSize: 14,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textSecondary,
  );

  static MlTextStyle bodySmall = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: MlColors.textMuted,
  );

  // Labels
  static MlTextStyle labelLarge = MlTextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static MlTextStyle labelMedium = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static MlTextStyle label = MlTextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: MlColors.textPrimary,
  );

  static MlTextStyle caption = MlTextStyle(
    fontSize: 12,
    fontWeight: FontWeight.w400,
    height: 1.3,
    color: MlColors.textMuted,
  );

  static MlTextStyle button = MlTextStyle(
    fontSize: 14,
    fontWeight: FontWeight.w600,
    height: 1.2,
    color: MlColors.textPrimary,
  );
}
