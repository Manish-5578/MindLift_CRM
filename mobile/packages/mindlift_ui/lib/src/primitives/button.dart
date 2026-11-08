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
    String? text,
    String? label,
    VoidCallback? onTap,
    VoidCallback? onPressed,
    this.variant = MlButtonVariant.primary,
    this.size = MlButtonSize.medium,
    this.icon,
    this.isLoading = false,
    this.isDisabled = false,
    this.fullWidth = false,
  })  : text = text ?? label ?? '',
        onTap = onTap ?? onPressed;

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

    final height = _resolveHeight();
    final padding = _resolvePadding();

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
          constraints: BoxConstraints(
            minHeight: MlSpacing.minTouchTarget,
            minWidth: MlSpacing.minTouchTarget,
          ),
          height: height,
          padding: padding,
          decoration: BoxDecoration(
            color: _isPressed && style.pressedColor != null
                ? style.pressedColor
                : style.backgroundColor,
            borderRadius: MlRadii.borderMd,
            border: style.borderColor != null
                ? Border.all(color: style.borderColor!, width: 1.5)
                : null,
          ),
          alignment: Alignment.center,
          child: content,
        ),
      ),
    );
  }

  double _resolveHeight() {
    switch (widget.size) {
      case MlButtonSize.small:
        return 36;
      case MlButtonSize.medium:
        return 44; // Meets touch target minimum
      case MlButtonSize.large:
        return 52;
    }
  }

  EdgeInsets _resolvePadding() {
    switch (widget.size) {
      case MlButtonSize.small:
        return const EdgeInsets.symmetric(horizontal: MlSpacing.sm);
      case MlButtonSize.medium:
        return const EdgeInsets.symmetric(horizontal: MlSpacing.md);
      case MlButtonSize.large:
        return const EdgeInsets.symmetric(horizontal: MlSpacing.lg);
    }
  }

  _ResolvedStyle _resolveStyle() {
    if (!_enabled) {
      return const _ResolvedStyle(
        backgroundColor: MlColors.surfaceElevated,
        textColor: MlColors.textDisabled,
      );
    }

    switch (widget.variant) {
      case MlButtonVariant.primary:
        return const _ResolvedStyle(
          backgroundColor: MlColors.primary,
          textColor: MlColors.white,
          pressedColor: MlColors.primaryHover,
        );
      case MlButtonVariant.secondary:
        return const _ResolvedStyle(
          backgroundColor: MlColors.surfaceElevated,
          textColor: MlColors.textPrimary,
          pressedColor: MlColors.surfaceLight,
        );
      case MlButtonVariant.outline:
        return const _ResolvedStyle(
          backgroundColor: Color(0x00000000),
          textColor: MlColors.primary,
          borderColor: MlColors.primary,
          pressedColor: Color(0x1A4F46E5),
        );
      case MlButtonVariant.danger:
        return const _ResolvedStyle(
          backgroundColor: MlColors.danger,
          textColor: MlColors.white,
          pressedColor: Color(0xFFDC2626),
        );
      case MlButtonVariant.ghost:
        return const _ResolvedStyle(
          backgroundColor: Color(0x00000000),
          textColor: MlColors.textSecondary,
          pressedColor: Color(0x1A94A3B8),
        );
    }
  }

  Widget _buildLoadingIndicator(Color color) {
    return SizedBox(
      width: 16,
      height: 16,
      child: CustomPaint(
        painter: _SpinnerPainter(color: color),
      ),
    );
  }
}

/// Compact 44x44 icon button primitive.
class MlIconButton extends StatefulWidget {
  const MlIconButton({
    super.key,
    required this.icon,
    VoidCallback? onTap,
    VoidCallback? onPressed,
    String? tooltip,
    String? label,
    this.backgroundColor = const Color(0x00000000),
  })  : onTap = onTap ?? onPressed,
        tooltip = tooltip ?? label ?? 'Button';

  final Widget icon;
  final VoidCallback? onTap;
  final String tooltip;
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
      4.71,
      false,
      paint,
    );
  }

  @override
  bool shouldRepaint(_SpinnerPainter oldDelegate) => oldDelegate.color != color;
}
