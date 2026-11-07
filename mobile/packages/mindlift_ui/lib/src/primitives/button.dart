import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

enum MlButtonVariant { primary, secondary, outline, danger, ghost }
enum MlButtonSize { small, medium, large }

/// Bespoke button primitive meeting the 44x44 minimum touch target.
class MlButton extends StatefulWidget {
  const MlButton({
    super.key,
    required this.text,
    this.onTap,
    this.variant = MlButtonVariant.primary,
    this.size = MlButtonSize.medium,
    this.icon,
    this.isLoading = false,
    this.isDisabled = false,
    this.fullWidth = false,
  });

  final String text;
  final VoidCallback? onTap;
  final MlButtonVariant variant;
  final MlButtonSize size;
  final Widget? icon;
  final bool isLoading;
  final bool isDisabled;
  final bool fullWidth;

  @override
  State<MlButton> createState() => _MlButtonState();
}

class _MlButtonState extends State<MlButton> {
  bool _isPressed = false;

  bool get _enabled => !widget.isDisabled && !widget.isLoading && widget.onTap != null;

  @override
  Widget build(BuildContext context) {
    final style = _resolveStyle();

    Widget content = Row(
      mainAxisSize: widget.fullWidth ? MainAxisSize.max : MainAxisSize.min,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        if (widget.isLoading) ...[
          _buildLoadingIndicator(style.textColor),
          const SizedBox(width: MlSpacing.xs),
        ] else if (widget.icon != null) ...[
          widget.icon!,
          const SizedBox(width: MlSpacing.xs),
        ],
        Text(
          widget.text,
          style: MlTypography.button(color: style.textColor),
        ),
      ],
    );

    return Semantics(
      button: true,
      enabled: _enabled,
      label: widget.text,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: _enabled ? widget.onTap : null,
        onTapDown: _enabled ? (_) => setState(() => _isPressed = true) : null,
        onTapUp: _enabled ? (_) => setState(() => _isPressed = false) : null,
        onTapCancel: _enabled ? () => setState(() => _isPressed = false) : null,
        child: Container(
          constraints: const BoxConstraints(
            minHeight: MlSpacing.minTouchTarget,
            minWidth: MlSpacing.minTouchTarget,
          ),
          padding: _paddingFor(widget.size),
          decoration: BoxDecoration(
            color: _isPressed ? style.pressedColor : style.backgroundColor,
            borderRadius: MlRadii.borderMd,
            border: style.borderColor != null
                ? Border.all(color: style.borderColor!, width: 1.5)
                : null,
          ),
          child: content,
        ),
      ),
    );
  }

  Widget _buildLoadingIndicator(Color color) {
    return SizedBox(
      width: 14,
      height: 14,
      child: CustomPaint(
        painter: _SpinnerPainter(color: color),
      ),
    );
  }

  EdgeInsetsGeometry _paddingFor(MlButtonSize size) => switch (size) {
        MlButtonSize.small => const EdgeInsets.symmetric(horizontal: MlSpacing.sm, vertical: 8),
        MlButtonSize.medium => const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
        MlButtonSize.large => const EdgeInsets.symmetric(horizontal: MlSpacing.xl, vertical: 14),
      };

  _ResolvedStyle _resolveStyle() {
    if (!_enabled) {
      return const _ResolvedStyle(
        backgroundColor: Color(0xFF334155),
        textColor: MlColors.textDisabled,
      );
    }

    return switch (widget.variant) {
      MlButtonVariant.primary => const _ResolvedStyle(
          backgroundColor: MlColors.primary,
          pressedColor: MlColors.primaryHover,
          textColor: Color(0xFFFFFFFF),
        ),
      MlButtonVariant.secondary => const _ResolvedStyle(
          backgroundColor: MlColors.surface,
          pressedColor: Color(0xFF334155),
          textColor: MlColors.textPrimary,
          borderColor: MlColors.border,
        ),
      MlButtonVariant.outline => const _ResolvedStyle(
          backgroundColor: Color(0x00000000),
          pressedColor: Color(0x1A4F46E5),
          textColor: MlColors.primaryOnDark,
          borderColor: MlColors.primary,
        ),
      MlButtonVariant.danger => const _ResolvedStyle(
          backgroundColor: MlColors.danger,
          pressedColor: Color(0xFFDC2626),
          textColor: Color(0xFFFFFFFF),
        ),
      MlButtonVariant.ghost => const _ResolvedStyle(
          backgroundColor: Color(0x00000000),
          pressedColor: Color(0x1FFFFFFF),
          textColor: MlColors.textSecondary,
        ),
    };
  }
}

/// Icon button primitive with guaranteed 44x44 touch target.
class MlIconButton extends StatefulWidget {
  const MlIconButton({
    super.key,
    required this.icon,
    this.onTap,
    this.tooltip,
    this.color,
    this.backgroundColor = const Color(0x00000000),
  });

  final Widget icon;
  final VoidCallback? onTap;
  final String? tooltip;
  final Color? color;
  final Color backgroundColor;

  @override
  State<MlIconButton> createState() => _MlIconButtonState();
}

class _MlIconButtonState extends State<MlIconButton> {
  bool _isPressed = false;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: widget.tooltip,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onTap,
        onTapDown: (_) => setState(() => _isPressed = true),
        onTapUp: (_) => setState(() => _isPressed = false),
        onTapCancel: () => setState(() => _isPressed = false),
        child: Container(
          width: MlSpacing.minTouchTarget,
          height: MlSpacing.minTouchTarget,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: _isPressed ? MlColors.surfaceElevated : widget.backgroundColor,
            borderRadius: MlRadii.borderMd,
          ),
          child: widget.icon,
        ),
      ),
    );
  }
}

class _ResolvedStyle {
  const _ResolvedStyle({
    required this.backgroundColor,
    required this.textColor,
    this.pressedColor,
    this.borderColor,
  });

  final Color backgroundColor;
  final Color textColor;
  final Color? pressedColor;
  final Color? borderColor;
}

class _SpinnerPainter extends CustomPainter {
  const _SpinnerPainter({required this.color});
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2
      ..style = PaintingStyle.stroke;
    canvas.drawArc(
      Rect.fromLTWH(0, 0, size.width, size.height),
      0,
      4.71, // 270 degrees
      false,
      paint,
    );
  }

  @override
  bool shouldRepaint(_SpinnerPainter oldDelegate) => oldDelegate.color != color;
}
