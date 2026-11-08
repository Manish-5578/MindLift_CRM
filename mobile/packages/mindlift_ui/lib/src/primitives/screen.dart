import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

/// Top-level screen primitive replacing Scaffold.
/// Strictly uses Flutter base primitives.
class MlScreen extends StatelessWidget {
  const MlScreen({
    super.key,
    required this.body,
    this.title,
    this.subtitle,
    this.leading,
    this.actions,
    this.bottomRail,
    this.banner,
    this.backgroundColor = MlColors.background,
    this.safeArea = true,
  });

  final Widget body;
  final String? title;
  final String? subtitle;
  final Widget? leading;
  final List<Widget>? actions;
  final Widget? bottomRail;
  final Widget? banner;
  final Color backgroundColor;
  final bool safeArea;

  @override
  Widget build(BuildContext context) {
    Widget content = Container(
      color: backgroundColor,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (banner != null) banner!,
          if (title != null || leading != null || actions != null)
            _buildHeader(context),
          Expanded(child: body),
          if (bottomRail != null) _buildBottomRail(context),
        ],
      ),
    );

    if (safeArea) {
      content = SafeArea(child: content);
    }

    return Directionality(
      textDirection: Directionality.maybeOf(context) ?? TextDirection.ltr,
      child: content,
    );
  }

  Widget _buildHeader(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: MlSpacing.md,
        vertical: MlSpacing.sm,
      ),
      decoration: const BoxDecoration(
        color: MlColors.background,
        border: Border(bottom: BorderSide(color: MlColors.border, width: 1)),
      ),
      constraints: const BoxConstraints(minHeight: MlSpacing.minTouchTarget),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          if (leading != null) ...[
            leading!,
            const SizedBox(width: MlSpacing.sm),
          ],
          if (title != null)
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    title!,
                    style: MlTypography.titleMedium(),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  if (subtitle != null)
                    Text(
                      subtitle!,
                      style: MlTypography.caption(),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                ],
              ),
            )
          else
            const Spacer(),
          if (actions != null) ...actions!,
        ],
      ),
    );
  }

  Widget _buildBottomRail(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(MlSpacing.md),
      decoration: const BoxDecoration(
        color: MlColors.surface,
        border: Border(top: BorderSide(color: MlColors.border, width: 1)),
      ),
      child: bottomRail!,
    );
  }
}

