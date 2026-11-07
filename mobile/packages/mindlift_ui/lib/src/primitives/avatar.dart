import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/typography.dart';

enum MlAvatarSize {
  sm(28, 11),
  md(36, 13),
  lg(44, 16),
  xl(64, 22);

  const MlAvatarSize(this.dimension, this.fontSize);
  final double dimension;
  final double fontSize;
}

/// Circular monogram or user avatar primitive without Material styling.
class MlAvatar extends StatelessWidget {
  const MlAvatar({
    super.key,
    this.name,
    this.imageUrl,
    this.size = MlAvatarSize.md,
    this.backgroundColor,
    this.textColor,
  });

  final String? name;
  final String? imageUrl;
  final MlAvatarSize size;
  final Color? backgroundColor;
  final Color? textColor;

  String _getInitials() {
    if (name == null || name!.trim().isEmpty) return '?';
    final parts = name!.trim().split(RegExp(r'\s+'));
    if (parts.length == 1) {
      return parts[0].substring(0, parts[0].length >= 2 ? 2 : 1).toUpperCase();
    }
    return '${parts[0][0]}${parts[1][0]}'.toUpperCase();
  }

  Color _deterministicBgColor() {
    if (backgroundColor != null) return backgroundColor!;
    if (name == null || name!.isEmpty) return MlColors.surfaceLight;
    final hash = name!.codeUnits.fold<int>(0, (prev, elem) => prev + elem);
    const palette = [
      Color(0xFF4F46E5), // Indigo
      Color(0xFF0EA5E9), // Sky
      Color(0xFF10B981), // Emerald
      Color(0xFFF59E0B), // Amber
      Color(0xFFEC4899), // Pink
      Color(0xFF8B5CF6), // Purple
      Color(0xFF14B8A6), // Teal
    ];
    return palette[hash % palette.length];
  }

  @override
  Widget build(BuildContext context) {
    final initials = _getInitials();
    final effectiveBg = _deterministicBgColor();
    final effectiveText = textColor ?? MlColors.white;

    Widget content;
    if (imageUrl != null && imageUrl!.isNotEmpty) {
      content = ClipOval(
        child: Image.network(
          imageUrl!,
          width: size.dimension,
          height: size.dimension,
          fit: BoxFit.cover,
          errorBuilder: (_, __, ___) => _buildInitials(initials, effectiveBg, effectiveText),
        ),
      );
    } else {
      content = _buildInitials(initials, effectiveBg, effectiveText);
    }

    return Semantics(
      label: name != null ? 'Avatar for $name' : 'User avatar',
      child: content,
    );
  }

  Widget _buildInitials(String initials, Color bg, Color text) {
    return Container(
      width: size.dimension,
      height: size.dimension,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: bg,
        shape: BoxShape.circle,
        border: Border.all(color: MlColors.border.withOpacity(0.5), width: 1.0),
      ),
      child: Text(
        initials,
        style: MlTypography.labelMedium.copyWith(
          fontSize: size.fontSize,
          fontWeight: FontWeight.w700,
          color: text,
          letterSpacing: 0.5,
        ),
      ),
    );
  }
}
