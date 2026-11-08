import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/locale_provider.dart';
import '../../../core/network_provider.dart';

class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authNotifierProvider);
    final language = ref.watch(localeProvider);
    final syncState = ref.watch(syncCoordinatorProvider);

    return MlScreen(
      title: 'More & Settings',
      subtitle: 'Field Application Preferences',
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // User Monogram & Info Card
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Row(
                children: [
                  MlAvatar(
                    name: authState.userFullName ?? '—',
                    size: MlAvatarSize.lg,
                  ),
                  const SizedBox(width: MlSpacing.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          authState.userFullName ?? '—',
                          style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          authState.userEmail ?? '',
                          style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
                        ),
                        const SizedBox(height: 4),
                        MlStatusChip(
                          label: authState.userRole ?? '—',
                          state: MlStatusState.primary,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Language & Directionality
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Language & Layout',
                    style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
                  ),
                  const SizedBox(height: MlSpacing.sm),
                  Text(
                    'Switch between English (Left-to-Right) and Arabic (Right-to-Left).',
                    style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
                  ),
                  const SizedBox(height: MlSpacing.md),
                  Row(
                    children: [
                      Expanded(
                        child: MlButton(
                          label: 'English (LTR)',
                          variant: language == AppLanguage.english
                              ? MlButtonVariant.primary
                              : MlButtonVariant.secondary,
                          onPressed: () {
                            ref.read(localeProvider.notifier).setLanguage(AppLanguage.english);
                          },
                        ),
                      ),
                      const SizedBox(width: MlSpacing.sm),
                      Expanded(
                        child: MlButton(
                          label: 'العربية (RTL)',
                          variant: language == AppLanguage.arabic
                              ? MlButtonVariant.primary
                              : MlButtonVariant.secondary,
                          onPressed: () {
                            ref.read(localeProvider.notifier).setLanguage(AppLanguage.arabic);
                          },
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Offline & Outbox Status
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Text(
                        'Offline Outbox',
                        style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
                      ),
                      MlStatusChip(
                        label: syncState.pendingCount == 0 ? 'Synchronized' : '${syncState.pendingCount} Queued',
                        state: syncState.pendingCount == 0 ? MlStatusState.success : MlStatusState.warning,
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.sm),
                  Text(
                    'Mutations created while disconnected are safely queued in encrypted storage and replayed atomically.',
                    style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
                  ),
                  const SizedBox(height: MlSpacing.md),
                  MlButton(
                    label: 'Sync Outbox Now',
                    onPressed: () async {
                      await ref.read(syncCoordinatorProvider.notifier).syncPendingMutations();
                      MlToast.show(context, message: 'Sync check completed.');
                    },
                    variant: MlButtonVariant.secondary,
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.xl),

            // Logout with confirmation
            MlButton(
              label: 'Sign Out & Wipe Local Cache',
              onPressed: () {
                MlDialog.show(
                  context: context,
                  title: 'Sign Out Confirmation',
                  message: 'This will purge the local encrypted database, clear tokens, and return to sign in.',
                  confirmLabel: 'Sign Out',
                  isDestructive: true,
                  onConfirm: () {
                    ref.read(authNotifierProvider.notifier).logout();
                  },
                );
              },
              variant: MlButtonVariant.danger,
            ),
          ],
        ),
      ),
    );
  }
}

