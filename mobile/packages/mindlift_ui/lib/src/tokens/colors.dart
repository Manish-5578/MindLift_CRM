import 'dart:ui';

/// Centralized color tokens for MindLift UI.
/// Tuned for a clean, professional, high-contrast light theme with soft slate accents.
class MlColors {
  const MlColors._();

  // Basic neutrals
  static const Color white = Color(0xFFFFFFFF);
  static const Color black = Color(0xFF000000);

  // Backgrounds & Surfaces (Light Theme)
  static const Color background = Color(0xFFF8FAFC);      // Slate 50 clean light background
  static const Color surface = Color(0xFFFFFFFF);         // Pure white surface card
  static const Color surfaceElevated = Color(0xFFF1F5F9); // Slate 100 subtle secondary surface
  static const Color surfaceLight = Color(0xFFF8FAFC);
  static const Color surfaceMuted = Color(0xFFE2E8F0);    // Slate 200 muted surface

  // Brand Primaries
  static const Color primary = Color(0xFF4F46E5);         // Indigo 600
  static const Color primaryHover = Color(0xFF4338CA);    // Indigo 700
  static const Color primarySoft = Color(0xFFEEF2FF);     // Soft Indigo 50 tint
  static const Color primaryLight = Color(0xFF6366F1);     // Indigo 500
  static const Color primaryOnDark = Color(0xFFFFFFFF);

  // Status & Accents (tuned for light background legibility)
  static const Color success = Color(0xFF10B981);          // Emerald 500
  static const Color successSoft = Color(0xFFD1FAE5);      // Emerald 100
  static const Color warning = Color(0xFFD97706);          // Amber 600
  static const Color warningSoft = Color(0xFFFEF3C7);      // Amber 100
  static const Color danger = Color(0xFFDC2626);           // Red 600
  static const Color dangerSoft = Color(0xFFFEE2E2);       // Red 100
  static const Color info = Color(0xFF0284C7);             // Sky 600
  static const Color infoSoft = Color(0xFFE0F2FE);         // Sky 100

  // Typography & Content (Dark high-contrast text on light background)
  static const Color textPrimary = Color(0xFF0F172A);      // Slate 900
  static const Color textSecondary = Color(0xFF334155);    // Slate 700
  static const Color textMuted = Color(0xFF64748B);        // Slate 500
  static const Color textDisabled = Color(0xFF94A3B8);     // Slate 400

  // Borders & Dividers
  static const Color border = Color(0xFFE2E8F0);           // Slate 200 clean light border
  static const Color borderLight = Color(0xFFCBD5E1);      // Slate 300
  static const Color divider = Color(0xFFE2E8F0);

  // Overlays
  static const Color overlay = Color(0x66000000);
}
