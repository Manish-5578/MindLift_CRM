import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../../core/push_service.dart';
import '../../leads/presentation/lead_actions.dart';

String _clock(int minute) {
  final h = minute ~/ 60;
  final m = minute % 60;
  final h12 = h % 12 == 0 ? 12 : h % 12;
  return '$h12:${m.toString().padLeft(2, '0')} ${h < 12 ? 'AM' : 'PM'}';
}

/// Quiet hours for this phone and which kinds of notification to receive.
class NotificationSettingsScreen extends ConsumerStatefulWidget {
  const NotificationSettingsScreen({super.key});

  @override
  ConsumerState<NotificationSettingsScreen> createState() => _NotificationSettingsScreenState();
}

class _NotificationSettingsScreenState extends ConsumerState<NotificationSettingsScreen> {
  bool _quiet = false;
  int _start = 22 * 60;
  int _end = 7 * 60;
  List<Map<String, dynamic>> _prefs = [];
  bool _digest = false;
  bool _loading = true;
  bool _failed = false;
  String? _installation;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final id = await ref.read(pushServiceProvider).installationId();
      final svc = ref.read(workspaceServiceProvider);
      final results = await Future.wait([svc.notificationPreferences(), ref.read(mobileDevicesServiceProvider).myDevices()]);
      final prefs = results[0] as Map<String, dynamic>;
      final devices = results[1] as List;
      final mine = devices.cast<dynamic>().where((d) => d.installationId == id);
      if (!mounted) return;
      setState(() {
        _installation = id;
        _prefs = ((prefs['preferences'] as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();
        _digest = prefs['dailyDigest'] == true;
        if (mine.isNotEmpty) {
          final d = mine.first;
          _quiet = d.quietStartMinute != null && d.quietEndMinute != null;
          _start = d.quietStartMinute ?? _start;
          _end = d.quietEndMinute ?? _end;
        }
        _failed = false;
      });
    } catch (_) {
      if (mounted) setState(() => _failed = true);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _saveQuiet() async {
    try {
      await ref.read(workspaceServiceProvider).setQuietHours(_installation ?? await ref.read(pushServiceProvider).installationId(), _quiet ? _start : null, _quiet ? _end : null);
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not save quiet hours.'), type: MlToastType.error);
    }
  }

  Future<void> _savePrefs() async {
    try {
      await ref.read(workspaceServiceProvider).saveNotificationPreferences(_prefs, _digest);
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not save your choices.'), type: MlToastType.error);
    }
  }

  Widget _toggle(String title, String? hint, bool value, ValueChanged<bool> onChanged) {
    return MlPressable(
      onTap: () => onChanged(!value),
      scale: 0.99,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 9),
        child: Row(children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(MlStrings.t(title), style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
              if (hint != null) Text(MlStrings.t(hint), style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
            ]),
          ),
          AnimatedContainer(
            duration: const Duration(milliseconds: 180),
            width: 46,
            height: 28,
            padding: const EdgeInsets.all(3),
            decoration: BoxDecoration(color: value ? MlColors.primary : MlColors.surfaceMuted, borderRadius: BorderRadius.circular(14)),
            alignment: value ? AlignmentDirectional.centerEnd : AlignmentDirectional.centerStart,
            child: Container(width: 22, height: 22, decoration: const BoxDecoration(color: MlColors.white, shape: BoxShape.circle)),
          ),
        ]),
      ),
    );
  }

  Widget _timeStepper(String label, int minute, ValueChanged<int> onChanged) {
    return Row(children: [
      Expanded(child: Text(label, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary))),
      MlIconButton(label: 'Earlier', icon: const MlIcon(MlIcons.chevronLeft, size: 26), onPressed: () => onChanged((minute - 30 + 1440) % 1440)),
      SizedBox(width: 84, child: Center(child: Text(_clock(minute), style: MlTypography.labelLarge.copyWith(color: MlColors.primaryText)))),
      MlIconButton(label: 'Later', icon: const MlIcon(MlIcons.chevronRight, size: 26), onPressed: () => onChanged((minute + 30) % 1440)),
    ]);
  }

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: 'Notification settings',
      leading: MlIconButton(label: 'Back', icon: const MlIcon(MlIcons.back, size: 24), onPressed: () => context.pop()),
      body: _loading
          ? const Center(child: MlSpinner(size: 28))
          : _failed
              ? MlEmptyState(title: 'Could not load your settings', icon: const MlIcon(MlIcons.warning, size: 30, color: MlColors.primary), actionLabel: 'Retry', onAction: () {
                  setState(() => _loading = true);
                  _load();
                })
              : ListView(padding: const EdgeInsets.all(MlSpacing.md), children: [
                  Container(
                    padding: const EdgeInsets.all(MlSpacing.md),
                    decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.lg), border: Border.all(color: MlColors.border)),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text('Quiet hours'.tr, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)),
                      const SizedBox(height: 2),
                      Text('This phone will not buzz between these times. Everything still shows in your notifications list.'.tr, style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                      const SizedBox(height: MlSpacing.xs),
                      _toggle('Turn on quiet hours', null, _quiet, (v) {
                        setState(() => _quiet = v);
                        _saveQuiet();
                      }),
                      if (_quiet) ...[
                        _timeStepper('From', _start, (m) {
                          setState(() => _start = m);
                          _saveQuiet();
                        }),
                        _timeStepper('Until', _end, (m) {
                          setState(() => _end = m);
                          _saveQuiet();
                        }),
                      ],
                    ]),
                  ),
                  const SizedBox(height: MlSpacing.md),
                  Container(
                    padding: const EdgeInsets.all(MlSpacing.md),
                    decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.lg), border: Border.all(color: MlColors.border)),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text('What to be notified about'.tr, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)),
                      const SizedBox(height: 2),
                      Text('Turn a kind off to stop its alerts and pushes.'.tr, style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                      const SizedBox(height: MlSpacing.xs),
                      for (var i = 0; i < _prefs.length; i++)
                        _toggle('${_prefs[i]['label'] ?? _prefs[i]['type']}', null, _prefs[i]['inApp'] == true, (v) {
                          setState(() => _prefs[i] = {..._prefs[i], 'inApp': v});
                          _savePrefs();
                        }),
                      _toggle('Daily summary email', 'Your day at a glance each morning', _digest, (v) {
                        setState(() => _digest = v);
                        _savePrefs();
                      }),
                    ]),
                  ),
                ]),
    );
  }
}
