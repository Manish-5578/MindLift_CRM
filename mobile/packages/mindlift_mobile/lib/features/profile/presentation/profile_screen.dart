import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/locale_provider.dart';
import '../../../core/network_provider.dart';

class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});

  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen> {
  bool _pushNotifications = true;
  bool _emailNotifications = true;
  bool _dealAlerts = true;
  bool _taskReminders = true;
  bool _biometricEnabled = true;

  void _handleSignOut() {
    MlDialog.show(
      context: context,
      title: 'Sign Out Confirmation',
      message: 'This will purge the local encrypted session tokens and return to the login screen.',
      confirmLabel: 'Sign Out',
      isDestructive: true,
      onConfirm: () {
        ref.read(authNotifierProvider.notifier).logout();
      },
    );
  }

  void _handleChangePassword() {
    MlDialog.show(
      context: context,
      title: 'Password Management',
      message: 'A password reset instructions link will be dispatched to your registered work email.',
      confirmLabel: 'Send Reset Link',
      onConfirm: () {
        MlToast.show(context, message: 'Password reset instructions dispatched.', type: MlToastType.success);
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    final authState = ref.watch(authNotifierProvider);
    final language = ref.watch(localeProvider);
    final syncState = ref.watch(syncCoordinatorProvider);
    final serverUrl = ref.watch(serverUrlProvider);

    final name = authState.userFullName ?? 'Sales Agent';
    final email = authState.userEmail ?? 'agent@mindlift.com';
    final role = authState.userRole ?? 'Field Staff';

    return MlScreen(
      title: 'My Profile',
      subtitle: name,
      actions: [
        MlIconButton(
          label: 'Sign Out',
          icon: const Text('🚪', style: TextStyle(fontSize: 18)),
          onPressed: _handleSignOut,
        ),
      ],
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // User Identity & Monogram Card
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
                boxShadow: const [
                  BoxShadow(
                    color: Color(0x080F172A),
                    blurRadius: 10,
                    offset: Offset(0, 2),
                  ),
                ],
              ),
              child: Row(
                children: [
                  MlAvatar(
                    name: name,
                    size: MlAvatarSize.lg,
                  ),
                  const SizedBox(width: MlSpacing.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          name,
                          style: MlTypography.headingSmall.copyWith(color: MlColors.textPrimary),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          email,
                          style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
                        ),
                        const SizedBox(height: 6),
                        Row(
                          children: [
                            MlStatusChip(
                              label: role,
                              state: MlStatusState.primary,
                            ),
                            const SizedBox(width: MlSpacing.xs),
                            MlStatusChip(
                              label: 'Active',
                              state: MlStatusState.success,
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Workspace & Organization Module
            _buildSectionHeader('Workspace & Environment'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                children: [
                  _buildInfoRow('Organization', 'MindLift Field Operations'),
                  _buildDivider(),
                  _buildInfoRow('Staging Server', serverUrl),
                  _buildDivider(),
                  _buildInfoRow('Connection', syncState.connectionState.name.toUpperCase()),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Notification Preferences Module
            _buildSectionHeader('Notification Preferences'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                children: [
                  _buildToggleRow('Push Notifications', 'Real-time alert pings', _pushNotifications, (v) => setState(() => _pushNotifications = v)),
                  _buildDivider(),
                  _buildToggleRow('Email Notifications', 'Daily digests & summaries', _emailNotifications, (v) => setState(() => _emailNotifications = v)),
                  _buildDivider(),
                  _buildToggleRow('Deal & Stage Alerts', 'Notifications on deal moves', _dealAlerts, (v) => setState(() => _dealAlerts = v)),
                  _buildDivider(),
                  _buildToggleRow('Overdue Task Reminders', 'Field task deadline alerts', _taskReminders, (v) => setState(() => _taskReminders = v)),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Language & Directionality Module
            _buildSectionHeader('Language & Layout'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Row(
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
            ),
            const SizedBox(height: MlSpacing.lg),

            // Security & Credentials Module
            _buildSectionHeader('Security & Biometrics'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                children: [
                  _buildToggleRow('Biometric Fingerprint / Face', 'Fast unlock on app resume', _biometricEnabled, (v) => setState(() => _biometricEnabled = v)),
                  _buildDivider(),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Account Password', style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                          Text('Last updated 30 days ago', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                        ],
                      ),
                      MlButton(
                        label: 'Change',
                        variant: MlButtonVariant.secondary,
                        onPressed: _handleChangePassword,
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Diagnostics & Offline Outbox
            _buildSectionHeader('Diagnostics & Offline Storage'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Encrypted Outbox', style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                          Text('${syncState.pendingCount} local mutations pending sync', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                        ],
                      ),
                      MlButton(
                        label: 'Sync Outbox',
                        variant: MlButtonVariant.secondary,
                        onPressed: () async {
                          await ref.read(syncCoordinatorProvider.notifier).syncPendingMutations();
                          MlToast.show(context, message: 'Sync check completed.');
                        },
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.xl),

            // Logout Option
            MlButton(
              label: 'Sign Out of MindLift',
              variant: MlButtonVariant.danger,
              onPressed: _handleSignOut,
            ),
            const SizedBox(height: MlSpacing.xl),
          ],
        ),
      ),
    );
  }

  Widget _buildSectionHeader(String title) {
    return Padding(
      padding: const EdgeInsets.only(bottom: MlSpacing.xs, left: MlSpacing.xs),
      child: Text(
        title,
        style: MlTypography.labelMedium.copyWith(color: MlColors.textSecondary, fontWeight: FontWeight.w700),
      ),
    );
  }

  Widget _buildInfoRow(String label, String value) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label, style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted)),
        const SizedBox(width: MlSpacing.sm),
        Expanded(
          child: Text(
            value,
            style: MlTypography.bodyMedium.copyWith(color: MlColors.textPrimary, fontWeight: FontWeight.w600),
            textAlign: TextAlign.end,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }

  Widget _buildToggleRow(String title, String subtitle, bool value, ValueChanged<bool> onChanged) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
              Text(subtitle, style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
            ],
          ),
        ),
        GestureDetector(
          onTap: () => onChanged(!value),
          child: Container(
            width: 44,
            height: 26,
            padding: const EdgeInsets.all(2),
            decoration: BoxDecoration(
              color: value ? MlColors.primary : MlColors.surfaceMuted,
              borderRadius: BorderRadius.circular(13),
            ),
            alignment: value ? Alignment.centerRight : Alignment.centerLeft,
            child: Container(
              width: 22,
              height: 22,
              decoration: const BoxDecoration(
                color: MlColors.white,
                shape: BoxShape.circle,
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildDivider() {
    return Container(
      height: 1,
      color: MlColors.border,
      margin: const EdgeInsets.symmetric(vertical: MlSpacing.sm),
    );
  }
}
