import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'pressable.dart';

enum MlButtonVariant { primary, secondary, outline, danger, ghost }
enum MlButtonSize { small, medium, large }

/// Button with Material-style heights (40 / 48 / 56), animated colour changes and press feedback.
class MlButton extends StatelessWidget {
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

  bool get _enabled => !isDisabled && !isLoading && onTap != null;

  @override
  Widget build(BuildContext context) {
    final style = _resolveStyle();
    final height = switch (size) {
      MlButtonSize.small => 40.0,
      MlButtonSize.medium => 48.0,
      MlButtonSize.large => 56.0,
    };
    final hPad = switch (size) {
      MlButtonSize.small => MlSpacing.md,
      MlButtonSize.medium => MlSpacing.lg,
      MlButtonSize.large => MlSpacing.xl,
    };

    final content = Row(
      mainAxisSize: fullWidth ? MainAxisSize.max : MainAxisSize.min,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        if (isLoading) ...[
          MlSpinner(color: style.textColor, size: 18),
          const SizedBox(width: MlSpacing.xs),
        ] else if (icon != null) ...[
          icon!,
          const SizedBox(width: MlSpacing.xs),
        ],
        Flexible(
          child: Text(
            text,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: MlTypography.button(color: style.textColor),
          ),
        ),
      ],
    );

    return Semantics(
      button: true,
      enabled: _enabled,
      label: text,
      child: MlPressable(
        onTap: _enabled ? onTap : null,
        scale: 0.97,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOutCubic,
          height: height,
          constraints: const BoxConstraints(minWidth: 64),
          padding: EdgeInsets.symmetric(horizontal: hPad),
          decoration: BoxDecoration(
            color: style.backgroundColor,
            borderRadius: BorderRadius.circular(MlRadii.lg),
            border: style.borderColor != null ? Border.all(color: style.borderColor!, width: 1.5) : null,
          ),
          alignment: Alignment.center,
          child: content,
        ),
      ),
    );
  }

  _ResolvedStyle _resolveStyle() {
    if (!_enabled && !isLoading) {
      return _ResolvedStyle(backgroundColor: MlColors.surfaceElevated, textColor: MlColors.textDisabled);
    }
    switch (variant) {
      case MlButtonVariant.primary:
        return const _ResolvedStyle(backgroundColor: MlColors.primary, textColor: MlColors.white);
      case MlButtonVariant.secondary:
        return _ResolvedStyle(backgroundColor: MlColors.surfaceElevated, textColor: MlColors.textPrimary);
      case MlButtonVariant.outline:
        return const _ResolvedStyle(backgroundColor: Color(0x00000000), textColor: MlColors.primary, borderColor: MlColors.primary);
      case MlButtonVariant.danger:
        return const _ResolvedStyle(backgroundColor: MlColors.danger, textColor: MlColors.white);
      case MlButtonVariant.ghost:
        return _ResolvedStyle(backgroundColor: const Color(0x00000000), textColor: MlColors.textSecondary);
    }
  }
}

/// 48 dp icon button with a round press highlight.
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
  bool _down = false;

  void _set(bool v) {
    if (_down != v && mounted) setState(() => _down = v);
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: widget.tooltip,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onTap,
        onTapDown: (_) => _set(true),
        onTapUp: (_) => _set(false),
        onTapCancel: () => _set(false),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 120),
          width: 48,
          height: 48,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: _down ? MlColors.textPrimary.withOpacity(0.10) : widget.backgroundColor,
            shape: BoxShape.circle,
          ),
          child: AnimatedScale(
            scale: _down ? 0.9 : 1,
            duration: const Duration(milliseconds: 120),
            child: widget.icon,
          ),
        ),
      ),
    );
  }
}

/// A smoothly rotating arc, shown while something loads.
class MlSpinner extends StatefulWidget {
  const MlSpinner({super.key, this.color, this.size = 20, this.strokeWidth = 2.4});
  final Color? color;
  final double size;
  final double strokeWidth;

  @override
  State<MlSpinner> createState() => _MlSpinnerState();
}

class _MlSpinnerState extends State<MlSpinner> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 900))..repeat();

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return RotationTransition(
      turns: _c,
      child: SizedBox(
        width: widget.size,
        height: widget.size,
        child: CustomPaint(painter: _SpinnerPainter(color: widget.color ?? MlColors.primary, stroke: widget.strokeWidth)),
      ),
    );
  }
}

class _ResolvedStyle {
  const _ResolvedStyle({required this.backgroundColor, required this.textColor, this.borderColor});
  final Color backgroundColor;
  final Color textColor;
  final Color? borderColor;
}

class _SpinnerPainter extends CustomPainter {
  const _SpinnerPainter({required this.color, required this.stroke});
  final Color color;
  final double stroke;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = stroke
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    canvas.drawArc(Rect.fromLTWH(stroke / 2, stroke / 2, size.width - stroke, size.height - stroke), 0, 4.4, false, paint);
  }

  @override
  bool shouldRepaint(_SpinnerPainter old) => old.color != color || old.stroke != stroke;
}
