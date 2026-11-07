import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

class MlTimelineItemData {
  const MlTimelineItemData({
    required this.id,
    required this.title,
    this.author,
    required this.timestamp,
    this.content,
    this.icon,
    this.indicatorColor = MlColors.primary,
    this.metadata,
  });

  final String id;
  final String title;
  final String? author;
  final DateTime timestamp;
  final String? content;
  final Widget? icon;
  final Color indicatorColor;
  final List<Widget>? metadata;
}

/// Chronological timeline primitive for Lead Notes, Call logs, and Audits.
/// Built strictly with Flutter base primitives, supporting LTR and RTL.
class MlTimeline extends StatelessWidget {
  const MlTimeline({
    super.key,
    required this.items,
    this.emptyPlaceholder,
  });

  final List<MlTimelineItemData> items;
  final Widget? emptyPlaceholder;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) {
      return emptyPlaceholder ??
          Container(
            padding: const EdgeInsets.all(MlSpacing.lg),
            alignment: Alignment.center,
            child: Text(
              'No activity history yet',
              style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
            ),
          );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (int i = 0; i < items.length; i++)
          _MlTimelineEntry(
            item: items[i],
            isFirst: i == 0,
            isLast: i == items.length - 1,
          ),
      ],
    );
  }
}

class _MlTimelineEntry extends StatelessWidget {
  const _MlTimelineEntry({
    required this.item,
    required this.isFirst,
    required this.isLast,
  });

  final MlTimelineItemData item;
  final bool isFirst;
  final bool isLast;

  String _formatTimestamp(DateTime dt) {
    final now = DateTime.now();
    final diff = now.difference(dt);
    if (diff.inMinutes < 1) return 'Just now';
    if (diff.inHours < 1) return '${diff.inMinutes}m ago';
    if (diff.inDays < 1) return '${diff.inHours}h ago';
    if (diff.inDays < 7) return '${diff.inDays}d ago';
    return '${dt.year}-${dt.month.toString().padLeft(2, '0')}-${dt.day.toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    final timeStr = _formatTimestamp(item.timestamp);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Timeline indicator rail
          SizedBox(
            width: 32,
            child: Stack(
              alignment: Alignment.topCenter,
              children: [
                // Connecting line
                Positioned(
                  top: isFirst ? 14 : 0,
                  bottom: isLast ? null : 0,
                  height: isLast ? 14 : null,
                  child: Container(
                    width: 2,
                    color: MlColors.border,
                  ),
                ),
                // Indicator dot/icon
                Positioned(
                  top: 8,
                  child: Container(
                    width: 16,
                    height: 16,
                    decoration: BoxDecoration(
                      color: item.indicatorColor,
                      shape: BoxShape.circle,
                      border: Border.all(color: MlColors.background, width: 2),
                    ),
                    alignment: Alignment.center,
                    child: item.icon,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: MlSpacing.xs),
          // Content box
          Expanded(
            child: Container(
              margin: const EdgeInsets.only(bottom: MlSpacing.md),
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.md),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          item.title,
                          style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      const SizedBox(width: MlSpacing.xs),
                      Text(
                        timeStr,
                        style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                      ),
                    ],
                  ),
                  if (item.author != null) ...[
                    const SizedBox(height: 2),
                    Text(
                      'by ${item.author}',
                      style: MlTypography.caption.copyWith(
                        color: MlColors.textMuted,
                        fontStyle: FontStyle.italic,
                      ),
                    ),
                  ],
                  if (item.content != null && item.content!.isNotEmpty) ...[
                    const SizedBox(height: MlSpacing.xs),
                    Text(
                      item.content!,
                      style: MlTypography.bodySmall.copyWith(color: MlColors.textPrimary),
                    ),
                  ],
                  if (item.metadata != null && item.metadata!.isNotEmpty) ...[
                    const SizedBox(height: MlSpacing.sm),
                    Wrap(
                      spacing: MlSpacing.xs,
                      runSpacing: MlSpacing.xs,
                      children: item.metadata!,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
