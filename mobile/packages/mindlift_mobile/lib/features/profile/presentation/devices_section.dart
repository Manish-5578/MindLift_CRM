import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/push_service.dart';
import '../../leads/presentation/lead_actions.dart';

String _ago(DateTime utc) {
  final d = DateTime.now().toUtc().difference(utc);
  if (d.inMinutes < 2) return 'Active now';
  if (d.inMinutes < 60) return '${d.inMinutes} min ago';
  if (d.inHours < 24) return '${d.inHours} h ago';
  if (d.inDays == 1) return 'Yesterday';
  return '${d.inDays} days ago';
}

/// The phones that receive this account's notifications, with a way to remove one, and a "sign out everywhere" action.
class DevicesSection extends ConsumerStatefulWidget {
  const DevicesSection({super.key});

  @override
  ConsumerState<DevicesSection> createState() => _DevicesSectionState();
}

class _DevicesSectionState extends ConsumerState<DevicesSection> {
  List<MobileDeviceDto>? _devices;
  String? _mine;
  bool _failed = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final mine = await ref.read(pushServiceProvider).installationId();
      final list = await ref.read(mobileDevicesServiceProvider).myDevices();
      if (mounted) {
        setState(() {
          _mine = mine;
          _devices = list;
          _failed = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _failed = true);
    }
  }

  void _remove(MobileDeviceDto d) {
    MlDialog.show(
      context: context,
      title: 'Remove this device?',
      message: 'It will stop receiving notifications from MindLift. It stays signed in until you sign out everywhere.',
      confirmLabel: 'Remove',
      isDestructive: true,
      onConfirm: () async {
        try {
          await ref.read(mobileDevicesServiceProvider).removeDevice(d.id);
          await _load();
          if (mounted) MlToast.show(context, message: 'Device removed.', type: MlToastType.success);
        } catch (e) {
          if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not remove the device.'), type: MlToastType.error);
        }
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    final devices = _devices;
    return Container(
      padding: const EdgeInsets.all(MlSpacing.md),
      decoration: BoxDecoration(
        color: MlColors.surface,
        borderRadius: BorderRadius.circular(MlRadii.lg),
        border: Border.all(color: MlColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (devices == null)
            _failed
                ? Row(children: [
                    Expanded(child: Text('Could not load your devices.'.tr, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))),
                    MlButton(label: 'Retry', size: MlButtonSize.small, variant: MlButtonVariant.secondary, onPressed: _load),
                  ])
                : const Padding(padding: EdgeInsets.all(8), child: Center(child: MlSpinner(size: 22)))
          else if (devices.isEmpty)
            Text('No devices are registered for notifications.'.tr, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))
          else
            for (var i = 0; i < devices.length; i++) ...[
              if (i > 0) Container(height: 1, color: MlColors.border, margin: const EdgeInsets.symmetric(vertical: MlSpacing.sm)),
              Row(
                children: [
                  Container(
                    width: 40,
                    height: 40,
                    decoration: BoxDecoration(color: MlColors.primary.withOpacity(0.12), borderRadius: BorderRadius.circular(12)),
                    alignment: Alignment.center,
                    child: const Icon(MlIcons.phone, size: 22, color: MlColors.primary),
                  ),
                  const SizedBox(width: MlSpacing.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Flexible(
                              child: Text(
                                '${devices[i].platform == 'ios' ? 'iPhone' : 'Android phone'} · v${devices[i].appVersion}',
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary),
                              ),
                            ),
                            if (devices[i].installationId == _mine) ...[
                              const SizedBox(width: 6),
                              const MlStatusChip(label: 'This device', state: MlStatusState.success),
                            ],
                          ],
                        ),
                        Text(_ago(devices[i].lastSeenAtUtc), style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                      ],
                    ),
                  ),
                  if (devices[i].installationId != _mine)
                    MlButton(label: 'Remove', size: MlButtonSize.small, variant: MlButtonVariant.ghost, onPressed: () => _remove(devices[i])),
                ],
              ),
            ],
        ],
      ),
    );
  }
}
