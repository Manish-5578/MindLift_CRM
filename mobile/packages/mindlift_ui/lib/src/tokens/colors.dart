import 'dart:ui';

/// Centralized color tokens for MindLift UI.
/// Supports high-contrast dark field mode and soft primary tinting.
class MlColors {
  const MlColors._();

  // Backgrounds & Surfaces
  static const Color background = Color(0xFF0F172A); // Deep Navy
  static const Color surface = Color(0xFF1E293B);    // Slate Surface Card
  static const Color surfaceElevated = Color(0xFF334155);
  static const Color surfaceMuted = Color(0xFF1E293B);

  // Brand Primaries
  static const Color primary = Color(0xFF4F46E5);      // Indigo / Primary Accent
  static const Color primaryHover = Color(0xFF4338CA);
  static const Color primarySoft = Color(0xFFEEF2FF);   // Soft Light Tint
  static const Color primaryOnDark = Color(0xFF818CF8);

  // Status & Accents
  static const Color success = Color(0xFF10B981);       // Emerald Green
  static const Color successSoft = Color(0xFFD1FAE5);
  static const Color warning = Color(0xFFF59E0B);       // Amber
  static const Color warningSoft = Color(0xFFFEF3C7);
  static const Color danger = Color(0xFFEF4444);        // Crimson
  static const Color dangerSoft = Color(0xFFFEE2E2);
  static const Color info = Color(0xFF38BDF8);          // Sky Blue
  static const Color infoSoft = Color(0xFFE0F2FE);

  // Typography & Content
  static const Color textPrimary = Color(0xFFF8FAFC);   // Crisp Light Text
  static const Color textSecondary = Color(0xFFCBD5E1); // Neutral Subtitle
  static const Color textMuted = Color(0xFF94A3B8);     // Muted Caption
  static const Color textDisabled = Color(0xFF64748B);

  // Borders & Dividers
  static const Color border = Color(0xFF334155);
  static const Color borderLight = Color(0xFF475569);
  static const Color divider = Color(0xFF1E293B);

  // Overlays
  static const Color overlay = Color(0x99000000);
}
