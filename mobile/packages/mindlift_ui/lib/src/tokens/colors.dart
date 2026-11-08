import 'dart:ui';

/// Centralized color tokens for MindLift UI.
/// Tuned for a clean, professional, high-contrast light theme with soft slate accents.
class MlColors {
  const MlColors._();

  /// Global mode flag; the app root rebuilds when it changes.
  static bool isDark = false;

  // Basic neutrals
  static const Color white = Color(0xFFFFFFFF);
  static const Color black = Color(0xFF000000);

  // Backgrounds & Surfaces (Light Theme)
  static Color get background => isDark ? const Color(0xFF0B1120) : Color(0xFFF8FAFC);      // Slate 50 clean light background
  static Color get surface => isDark ? const Color(0xFF151C2C) : Color(0xFFFFFFFF);         // Pure white surface card
  static Color get surfaceElevated => isDark ? const Color(0xFF1E2638) : Color(0xFFF1F5F9); // Slate 100 subtle secondary surface
  static Color get surfaceLight => isDark ? const Color(0xFF0B1120) : Color(0xFFF8FAFC);
  static Color get surfaceMuted => isDark ? const Color(0xFF2A3347) : Color(0xFFE2E8F0);    // Slate 200 muted surface

  // Brand Primaries
  static const Color primary = Color(0xFF4F46E5);         // Indigo 600
  static const Color primaryHover = Color(0xFF4338CA);    // Indigo 700
  static Color get primarySoft => isDark ? const Color(0xFF26264F) : Color(0xFFEEF2FF);     // Soft Indigo 50 tint
  static const Color primaryLight = Color(0xFF6366F1);     // Indigo 500
  static const Color primaryOnDark = Color(0xFFFFFFFF);

  // Status & Accents (tuned for light background legibility)
  static const Color success = Color(0xFF10B981);          // Emerald 500
  static Color get successSoft => isDark ? const Color(0xFF0F3D2E) : Color(0xFFD1FAE5);      // Emerald 100
  static const Color warning = Color(0xFFD97706);          // Amber 600
  static Color get warningSoft => isDark ? const Color(0xFF3F2E0E) : Color(0xFFFEF3C7);      // Amber 100
  static const Color danger = Color(0xFFDC2626);           // Red 600
  static Color get dangerSoft => isDark ? const Color(0xFF45181B) : Color(0xFFFEE2E2);       // Red 100
  static const Color info = Color(0xFF0284C7);             // Sky 600
  static Color get infoSoft => isDark ? const Color(0xFF0C3447) : Color(0xFFE0F2FE);         // Sky 100

  // Typography & Content (Dark high-contrast text on light background)
  static Color get textPrimary => isDark ? const Color(0xFFF1F5F9) : Color(0xFF0F172A);      // Slate 900
  static Color get textSecondary => isDark ? const Color(0xFFCBD5E1) : Color(0xFF334155);    // Slate 700
  static Color get textMuted => isDark ? const Color(0xFF94A3B8) : Color(0xFF64748B);        // Slate 500
  static Color get textDisabled => isDark ? const Color(0xFF64748B) : Color(0xFF94A3B8);     // Slate 400

  // Borders & Dividers
  static Color get border => isDark ? const Color(0xFF2A3347) : Color(0xFFE2E8F0);           // Slate 200 clean light border
  static Color get borderLight => isDark ? const Color(0xFF3B465E) : Color(0xFFCBD5E1);      // Slate 300
  static Color get divider => isDark ? const Color(0xFF2A3347) : Color(0xFFE2E8F0);

  // Overlays
  static const Color overlay = Color(0x66000000);
}
