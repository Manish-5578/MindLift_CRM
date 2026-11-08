import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

enum MlSyncStatus { online, offline, syncing, conflict, error }

typedef MlConnectionState = MlSyncStatus;

/// In-header permanent connection & sync status banner.
class MlConnectionBanner extends StatelessWidget {
  const MlConnectionBanner({
    super.key,
    MlSyncStatus? status,
    MlSyncStatus? state,
    this.pendingCount = 0,
    this.onRetry,
  }) : status = status ?? state ?? MlSyncStatus.online;

  final MlSyncStatus status;
  final int pendingCount;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    if (status == MlSyncStatus.online && pendingCount == 0) {
      return const SizedBox.shrink();
    }

    final config = _configFor(status);

    return Container(
      color: config.backgroundColor,
      padding: const EdgeInsets.symmetric(
        horizontal: MlSpacing.md,
        vertical: MlSpacing.xs,
      ),
      child: Row(
        children: [
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(
              color: config.indicatorColor,
              shape: BoxShape.circle,
            ),
          ),
          const SizedBox(width: MlSpacing.xs),
          Expanded(
            child: Text(
              _message(config.label),
              style: MlTypography.caption.copyWith(color: config.textColor),
            ),
          ),
          if (onRetry != null && (status == MlSyncStatus.offline || status == MlSyncStatus.error))
            GestureDetector(
              onTap: onRetry,
              child: Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: MlSpacing.xs,
                  vertical: 2,
                ),
                decoration: BoxDecoration(
                  color: MlColors.surface,
                  borderRadius: MlRadii.borderSm,
                ),
                child: Text(
                  'Retry',
                  style: MlTypography.caption.copyWith(
                    color: MlColors.primary,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }

  String _message(String baseLabel) {
    if (pendingCount > 0) {
      return '$baseLabel • $pendingCount changes queued locally';
    }
    return baseLabel;
  }

  _BannerConfig _configFor(MlSyncStatus status) {
    switch (status) {
      case MlSyncStatus.online:
        return const _BannerConfig(
          label: 'Connected',
          backgroundColor: Color(0xFF064E3B),
          indicatorColor: MlColors.success,
          textColor: Color(0xFFD1FAE5),
        );
      case MlSyncStatus.offline:
        return const _BannerConfig(
          label: 'Offline — changes queued locally',
          backgroundColor: Color(0xFF78350F),
          indicatorColor: MlColors.warning,
          textColor: Color(0xFFFEF3C7),
        );
      case MlSyncStatus.syncing:
        return const _BannerConfig(
          label: 'Syncing changes with MindLift...',
          backgroundColor: Color(0xFF1E1B4B),
          indicatorColor: MlColors.primary,
          textColor: Color(0xFFE0E7FF),
        );
      case MlSyncStatus.conflict:
        return const _BannerConfig(
          label: 'Sync conflict detected — review needed',
          backgroundColor: Color(0xFF7F1D1D),
          indicatorColor: MlColors.danger,
          textColor: Color(0xFFFEE2E2),
        );
      case MlSyncStatus.error:
        return const _BannerConfig(
          label: 'Sync paused — network error',
          backgroundColor: Color(0xFF7F1D1D),
          indicatorColor: MlColors.danger,
          textColor: Color(0xFFFEE2E2),
        );
    }
  }
}

class _BannerConfig {
  const _BannerConfig({
    required this.label,
    required this.backgroundColor,
    required this.indicatorColor,
    required this.textColor,
  });

  final String label;
  final Color backgroundColor;
  final Color indicatorColor;
  final Color textColor;
}
