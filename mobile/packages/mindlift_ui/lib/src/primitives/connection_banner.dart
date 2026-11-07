import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

enum MlSyncStatus { online, offline, syncing, conflict, error }

/// In-header permanent connection & sync status banner.
class MlConnectionBanner extends StatelessWidget {
  const MlConnectionBanner({
    super.key,
    required this.status,
    this.pendingCount = 0,
    this.onRetry,
  });

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
              style: MlTypography.caption(color: config.textColor),
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
                  border: Border.all(color: config.textColor, width: 1),
                  borderRadius: MlRadii.borderSm,
                ),
                child: Text(
                  'Retry',
                  style: MlTypography.caption(color: config.textColor),
                ),
              ),
            ),
        ],
      ),
    );
  }

  String _message(String base) {
    if (pendingCount > 0) {
      return '$base ($pendingCount pending changes)';
    }
    return base;
  }

  _BannerConfig _configFor(MlSyncStatus status) => switch (status) {
        MlSyncStatus.online => const _BannerConfig(
            backgroundColor: Color(0xFF064E3B),
            indicatorColor: MlColors.success,
            textColor: MlColors.textPrimary,
            label: 'Connected',
          ),
        MlSyncStatus.syncing => const _BannerConfig(
            backgroundColor: Color(0xFF1E3A8A),
            indicatorColor: MlColors.info,
            textColor: MlColors.textPrimary,
            label: 'Syncing changes…',
          ),
        MlSyncStatus.offline => const _BannerConfig(
            backgroundColor: Color(0xFF78350F),
            indicatorColor: MlColors.warning,
            textColor: MlColors.textPrimary,
            label: 'Working Offline',
          ),
        MlSyncStatus.conflict => const _BannerConfig(
            backgroundColor: Color(0xFF7F1D1D),
            indicatorColor: MlColors.danger,
            textColor: MlColors.textPrimary,
            label: 'Sync Conflict Detected',
          ),
        MlSyncStatus.error => const _BannerConfig(
            backgroundColor: Color(0xFF7F1D1D),
            indicatorColor: MlColors.danger,
            textColor: MlColors.textPrimary,
            label: 'Sync Interrupted',
          ),
      };
}

class _BannerConfig {
  const _BannerConfig({
    required this.backgroundColor,
    required this.indicatorColor,
    required this.textColor,
    required this.label,
  });

  final Color backgroundColor;
  final Color indicatorColor;
  final Color textColor;
  final String label;
}
