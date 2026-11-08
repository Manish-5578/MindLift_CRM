import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

/// Dense, tactile card primitive for CRM records (Leads, Deals, Activities).
/// Adheres strictly to MindLift custom tokens and zero-native-UI rules.
class MlRecordCard extends StatefulWidget {
  const MlRecordCard({
    super.key,
    required this.title,
    this.subtitle,
    this.leading,
    this.trailing,
    this.statusChip,
    this.metadata,
    this.onTap,
    this.backgroundColor = MlColors.surface,
    this.borderColor = MlColors.border,
    this.padding = const EdgeInsets.all(MlSpacing.md),
  });

  final String title;
  final String? subtitle;
  final Widget? leading;
  final Widget? trailing;
  final Widget? statusChip;
  final List<Widget>? metadata;
  final VoidCallback? onTap;
  final Color backgroundColor;
  final Color borderColor;
  final EdgeInsetsGeometry padding;

  @override
  State<MlRecordCard> createState() => _MlRecordCardState();
}

class _MlRecordCardState extends State<MlRecordCard> {
  bool _isPressed = false;

  @override
  Widget build(BuildContext context) {
    final cardContent = Container(
      constraints: const BoxConstraints(minHeight: MlSpacing.touchTarget),
      padding: widget.padding,
      decoration: BoxDecoration(
        color: _isPressed ? widget.backgroundColor.withOpacity(0.85) : widget.backgroundColor,
        borderRadius: BorderRadius.circular(MlRadii.lg),
        border: Border.all(color: widget.borderColor, width: 1.0),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (widget.leading != null) ...[
                widget.leading!,
                const SizedBox(width: MlSpacing.sm),
              ],
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      widget.title,
                      style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    if (widget.subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(
                        widget.subtitle!,
                        style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                  ],
                ),
              ),
              if (widget.statusChip != null) ...[
                const SizedBox(width: MlSpacing.sm),
                widget.statusChip!,
              ],
              if (widget.trailing != null) ...[
                const SizedBox(width: MlSpacing.sm),
                widget.trailing!,
              ],
            ],
          ),
          if (widget.metadata != null && widget.metadata!.isNotEmpty) ...[
            const SizedBox(height: MlSpacing.sm),
            Wrap(
              spacing: MlSpacing.xs,
              runSpacing: MlSpacing.xs,
              children: widget.metadata!,
            ),
          ],
        ],
      ),
    );

    if (widget.onTap == null) {
      return cardContent;
    }

    return Semantics(
      button: true,
      label: widget.title,
      child: GestureDetector(
        onTapDown: (_) => setState(() => _isPressed = true),
        onTapUp: (_) => setState(() => _isPressed = false),
        onTapCancel: () => setState(() => _isPressed = false),
        onTap: widget.onTap,
        behavior: HitTestBehavior.opaque,
        child: cardContent,
      ),
    );
  }
}

