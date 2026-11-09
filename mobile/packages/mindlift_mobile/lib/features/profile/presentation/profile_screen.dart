import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/locale_provider.dart';
import '../../../core/network_provider.dart';
import '../../../core/offline_store.dart';
import '../../../core/push_service.dart';
import '../../../core/settings_provider.dart';
import '../../../core/top_bar_actions.dart';
import 'package:image_picker/image_picker.dart';
import '../../leads/presentation/lead_actions.dart';
import '../../workspace/workspace_widgets.dart' show UserAvatar;
import 'devices_section.dart';

class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});

  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen> {
  Future<void> _testPush() async {
    try {
      final results = await ref.read(mobileDevicesServiceProvider).sendTestPush();
      if (!mounted) return;
      MlDialog.show(
        context: context,
        title: 'Test notification',
        message: results.isEmpty ? 'The server returned no result.' : results.join(String.fromCharCode(10)),
        confirmLabel: 'OK',
        onConfirm: () {},
      );
    } catch (e) {
      if (mounted) MlToast.show(context, message: 'Test failed: $e');
    }
  }

  int _photoVersion = 0;

  Future<void> _changePhoto() async {
    final id = ref.read(authNotifierProvider).userId;
    await MlSheet.show<void>(
      context: context,
      title: 'Profile photo',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          MlButton(label: 'Take a photo', icon: MlIcon(MlIcons.camera, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
            Navigator.of(sheetContext).pop();
            _pickPhoto(ImageSource.camera, id);
          }),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'Choose from gallery', icon: MlIcon(MlIcons.gallery, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
            Navigator.of(sheetContext).pop();
            _pickPhoto(ImageSource.gallery, id);
          }),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'Remove photo', variant: MlButtonVariant.ghost, onPressed: () async {
            Navigator.of(sheetContext).pop();
            try {
              await ref.read(workspaceServiceProvider).removeAvatar();
              UserAvatar.forget(id);
              if (mounted) setState(() => _photoVersion++);
            } catch (e) {
              if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not remove the photo.'), type: MlToastType.error);
            }
          }),
        ]),
      ),
    );
  }

  Future<void> _pickPhoto(ImageSource source, String? id) async {
    try {
      final x = await ImagePicker().pickImage(source: source, imageQuality: 80, maxWidth: 800, maxHeight: 800);
      if (x == null) return;
      await ref.read(workspaceServiceProvider).uploadAvatar(path: x.path, name: x.name.isEmpty ? 'me.jpg' : x.name);
      UserAvatar.forget(id);
      if (mounted) {
        setState(() => _photoVersion++);
        MlToast.show(context, message: 'Photo updated.', type: MlToastType.success);
      }
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not update the photo.'), type: MlToastType.error);
    }
  }

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

  void _handleSignOutEverywhere() {
    MlDialog.show(
      context: context,
      title: 'Sign out of all devices?',
      message: 'Every phone and browser signed in to your account will be signed out, this one included. You will need to sign in again.',
      confirmLabel: 'Sign out everywhere',
      isDestructive: true,
      onConfirm: () async {
        try {
          await ref.read(authServiceProvider).logoutEverywhere();
        } catch (e) {
          if (mounted) MlToast.show(context, message: 'Could not reach the server. Try again when you are online.', type: MlToastType.error);
          return;
        }
        await ref.read(authNotifierProvider.notifier).logout();
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
    final outbox = ref.watch(outboxProvider);
    final serverUrl = ref.watch(serverUrlProvider);

    final name = authState.userFullName ?? '—';
    final email = authState.userEmail ?? '';
    final role = authState.userRole ?? '—';
    final title = authState.userJobTitle;

    return MlScreen(
      title: 'My Profile',
      subtitle: name,
      actions: [
        ...topBarActions(),
        MlIconButton(
          label: 'Sign Out',
          icon: MlIcon(MlIcons.logout, size: 18),
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
                  MlPressable(
                    onTap: _changePhoto,
                    child: Stack(
                      clipBehavior: Clip.none,
                      children: [
                        UserAvatar(userId: authState.userId, name: name, size: MlAvatarSize.xl, version: _photoVersion),
                        Positioned(
                          right: -2,
                          bottom: -2,
                          child: Container(
                            width: 24,
                            height: 24,
                            decoration: BoxDecoration(color: MlColors.primary, shape: BoxShape.circle, border: Border.all(color: MlColors.surface, width: 2)),
                            alignment: Alignment.center,
                            child: const Icon(MlIcons.camera, size: 13, color: MlColors.white),
                          ),
                        ),
                      ],
                    ),
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
                        if (title != null && title.isNotEmpty) ...[
                          const SizedBox(height: 2),
                          Text(title, style: MlTypography.bodyMedium.copyWith(color: MlColors.textSecondary, fontWeight: FontWeight.w600)),
                        ],
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
                  _buildToggleRow('Biometric Fingerprint / Face', 'Unlock with the fingerprint/face registered on this device', ref.watch(settingsProvider).biometricLock, (v) async {
                    final ok = await ref.read(settingsProvider.notifier).setBiometricLock(v);
                    if (!ok && mounted) {
                      MlToast.show(context, message: v ? 'Set up a fingerprint, face or screen lock on this device first.' : 'Could not update biometric setting.');
                    }
                  }),
                  _buildDivider(),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Account Password'.tr, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
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

            // Devices
            _buildSectionHeader('My devices'),
            const DevicesSection(),
            const SizedBox(height: MlSpacing.lg),

            // Offline changes
            _buildSectionHeader('Offline & sync'),
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Container(
                        width: 10,
                        height: 10,
                        decoration: BoxDecoration(color: outbox.online ? MlColors.success : MlColors.warning, shape: BoxShape.circle),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          outbox.online
                              ? (outbox.pending == 0 ? 'Online. Everything is synced.' : '${outbox.pending} change${outbox.pending == 1 ? '' : 's'} waiting to sync')
                              : 'Offline. ${outbox.pending} change${outbox.pending == 1 ? '' : 's'} saved on this phone.',
                          style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary),
                        ),
                      ),
                      MlButton(
                        label: outbox.syncing ? 'Syncing' : 'Sync now',
                        size: MlButtonSize.small,
                        variant: MlButtonVariant.secondary,
                        onPressed: outbox.syncing ? null : () => ref.read(outboxProvider.notifier).flush(),
                      ),
                    ],
                  ),
                  if (outbox.lastSyncedAt != null) ...[
                    const SizedBox(height: 4),
                    Text('Last synced ${outbox.lastSyncedAt!.toLocal().hour.toString().padLeft(2, '0')}:${outbox.lastSyncedAt!.toLocal().minute.toString().padLeft(2, '0')}',
                        style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                  ],
                  for (final item in outbox.failed) ...[
                    _buildDivider(),
                    Text(item.label, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                    const SizedBox(height: 2),
                    Text(item.error ?? 'Not saved', style: MlTypography.caption.copyWith(color: MlColors.danger)),
                    const SizedBox(height: MlSpacing.xs),
                    Row(
                      children: [
                        MlButton(label: 'Retry', size: MlButtonSize.small, variant: MlButtonVariant.secondary, onPressed: () => ref.read(outboxProvider.notifier).retry(item.id)),
                        const SizedBox(width: MlSpacing.sm),
                        MlButton(label: 'Discard', size: MlButtonSize.small, variant: MlButtonVariant.ghost, onPressed: () => ref.read(outboxProvider.notifier).discard(item.id)),
                      ],
                    ),
                  ],
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.sm),
            MlButton(label: 'Check push delivery', variant: MlButtonVariant.secondary, onPressed: _testPush),
            const SizedBox(height: MlSpacing.xl),

            // Logout Option
            MlButton(
              label: 'Sign Out of MindLift',
              variant: MlButtonVariant.danger,
              onPressed: _handleSignOut,
            ),
            const SizedBox(height: MlSpacing.sm),
            MlButton(
              label: 'Sign out of all devices',
              variant: MlButtonVariant.ghost,
              onPressed: _handleSignOutEverywhere,
            ),
            const SizedBox(height: MlSpacing.xl),
          ],
        ),
      ),
    );
  }

  Widget _buildSectionHeader(String title) {
    return Padding(
      padding: const EdgeInsetsDirectional.only(bottom: MlSpacing.xs, start: MlSpacing.xs),
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
            alignment: value ? AlignmentDirectional.centerEnd : AlignmentDirectional.centerStart,
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
