import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../core/network_provider.dart';
import '../../core/offline_store.dart';
import '../../core/top_bar_actions.dart';
import '../leads/presentation/lead_actions.dart';
import '../quick_add/presentation/quick_add_sheet.dart';
import 'workspace_widgets.dart';

const _months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const _days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

DateTime _day(DateTime d) => DateTime(d.year, d.month, d.day);

/// Week strip plus the day's agenda: tasks, calls, meetings and notes by date.
class CalendarScreen extends ConsumerStatefulWidget {
  const CalendarScreen({super.key});

  @override
  ConsumerState<CalendarScreen> createState() => _CalendarScreenState();
}

class _CalendarScreenState extends ConsumerState<CalendarScreen> {
  List<Map<String, dynamic>> _all = [];
  DateTime _selected = _day(DateTime.now());
  late DateTime _weekStart = _startOfWeek(_selected);
  bool _loading = true;
  bool _offline = false;
  String? _error;

  static DateTime _startOfWeek(DateTime d) => _day(d).subtract(Duration(days: d.weekday - 1));

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      // The API returns at most 200 per page, so read several pages: this covers well past and well ahead of today.
      final svc = ref.read(workspaceServiceProvider);
      var p = await svc.myActivities();
      final items = [...p.items];
      for (var page = 2; p.hasMore && page <= 6; page++) {
        p = await svc.myActivities(page: page);
        items.addAll(p.items);
      }
      p = RecordPage(items: items, total: p.total, page: 1, pageSize: items.length);
      if (!mounted) return;
      setState(() {
        _all = p.items;
        _offline = false;
        _error = null;
      });
      unawaited(ref.read(offlineStoreProvider).putCache('calendar', p.toJson()));
    } catch (e) {
      if (isOfflineError(e)) {
        ref.read(outboxProvider.notifier).markOffline();
        final saved = await ref.read(offlineStoreProvider).getCache('calendar');
        if (mounted) {
          if (saved != null) {
            setState(() {
              _all = RecordPage.fromJson(Map<String, dynamic>.from(saved.data as Map)).items;
              _offline = true;
            });
          } else {
            setState(() => _error = 'You are offline and the calendar has not been opened before.');
          }
        }
      } else if (mounted) {
        setState(() => _error = 'We could not load your calendar. Pull down to retry.');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  DateTime? _due(Map<String, dynamic> a) => DateTime.tryParse(str(a['dueAtUtc']))?.toLocal();

  List<Map<String, dynamic>> _on(DateTime d) {
    final list = _all.where((a) {
      final due = _due(a);
      return due != null && _day(due) == d;
    }).toList()
      ..sort((a, b) => _due(a)!.compareTo(_due(b)!));
    return list;
  }

  bool _hasOpen(DateTime d) => _all.any((a) {
        final due = _due(a);
        return due != null && _day(due) == d && str(a['status']) != 'Completed';
      });

  Future<void> _complete(Map<String, dynamic> a) async {
    try {
      final sent = await sendOrQueue(
        ref,
        send: () => ref.read(workspaceServiceProvider).completeActivity(str(a['id'])),
        method: 'POST',
        path: '/api/v1/activities/${a['id']}/complete',
        body: {'outcome': null, 'notes': null, 'followUp': null},
        label: 'Complete ${str(a['subject'])}',
      );
      if (!mounted) return;
      MlToast.show(context, message: sent ? 'Marked complete.' : 'Saved on this phone. It will sync when you are back online.', type: MlToastType.success);
      if (sent) await _load();
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not complete this.'), type: MlToastType.error);
    }
  }

  void _shiftWeek(int weeks) => setState(() {
        _weekStart = _weekStart.add(Duration(days: 7 * weeks));
        _selected = _weekStart.add(Duration(days: _selected.weekday - 1));
      });

  void _today() => setState(() {
        _selected = _day(DateTime.now());
        _weekStart = _startOfWeek(_selected);
      });

  static IconData _icon(String type) => switch (type) {
        'Call' => MlIcons.call,
        'Note' => MlIcons.note,
        'Meeting' => MlIcons.handshake,
        _ => MlIcons.checkCircle,
      };

  @override
  Widget build(BuildContext context) {
    final today = _day(DateTime.now());
    final overdue = _all.where((a) {
      final due = _due(a);
      return due != null && str(a['status']) != 'Completed' && _day(due).isBefore(today);
    }).length;
    final items = _on(_selected);
    final month = _weekStart.month == _weekStart.add(const Duration(days: 6)).month
        ? '${_months[_weekStart.month - 1]} ${_weekStart.year}'
        : '${_months[_weekStart.month - 1].substring(0, 3)} – ${_months[_weekStart.add(const Duration(days: 6)).month - 1].substring(0, 3)} ${_weekStart.add(const Duration(days: 6)).year}';

    return MlScreen(
      title: 'Calendar',
      subtitle: _offline ? 'Saved copy · you are offline' : (overdue > 0 ? '$overdue overdue' : 'You are up to date'),
      leading: MlIconButton(label: 'Back', icon: const MlIcon(MlIcons.back, size: 24), onPressed: () => context.canPop() ? context.pop() : context.go('/home')),
      actions: [
        MlIconButton(label: 'Add', icon: const MlIcon(MlIcons.add, size: 26, color: MlColors.primary), onPressed: () async {
          QuickAddSheet.show(context);
        }),
        ...topBarActions(),
      ],
      body: Column(
        children: [
          Container(
            padding: const EdgeInsets.fromLTRB(MlSpacing.sm, 0, MlSpacing.sm, MlSpacing.sm),
            decoration: BoxDecoration(color: MlColors.surface, border: Border(bottom: BorderSide(color: MlColors.border))),
            child: Column(
              children: [
                Row(children: [
                  MlIconButton(label: 'Previous week', icon: const MlIcon(MlIcons.chevronLeft, size: 26), onPressed: () => _shiftWeek(-1)),
                  Expanded(child: Center(child: Text(month, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)))),
                  MlButton(label: 'Today', size: MlButtonSize.small, variant: MlButtonVariant.ghost, onPressed: _today),
                  MlIconButton(label: 'Next week', icon: const MlIcon(MlIcons.chevronRight, size: 26), onPressed: () => _shiftWeek(1)),
                ]),
                Row(children: [
                  for (var i = 0; i < 7; i++) Expanded(child: _dayCell(_weekStart.add(Duration(days: i)), today)),
                ]),
              ],
            ),
          ),
          Expanded(
            child: MlPullToRefresh(
              onRefresh: _load,
              child: _loading
                  ? ListView(padding: const EdgeInsets.all(MlSpacing.md), children: [for (var i = 0; i < 4; i++) const Padding(padding: EdgeInsets.only(bottom: MlSpacing.sm), child: MlSkeleton(height: 64, borderRadius: MlRadii.lg))])
                  : items.isEmpty
                      ? ListView(physics: const AlwaysScrollableScrollPhysics(), children: [
                          const SizedBox(height: 50),
                          MlEmptyState(
                            title: _error ?? (_selected == today ? 'Nothing on today' : 'Nothing on this day'),
                            icon: MlIcon(_error != null ? MlIcons.warning : MlIcons.checkCircle, size: 30, color: MlColors.primary),
                            subtitle: _error == null ? 'Tap + to add a task, call or note.' : null,
                          ),
                        ])
                      : ListView(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(MlSpacing.md),
                          children: [for (final a in items) _agendaRow(a)],
                        ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _dayCell(DateTime d, DateTime today) {
    final sel = d == _selected;
    final isToday = d == today;
    final dot = _hasOpen(d);
    return MlPressable(
      onTap: () => setState(() => _selected = d),
      scale: 0.92,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 200),
        curve: Curves.easeOutCubic,
        margin: const EdgeInsets.symmetric(horizontal: 2),
        padding: const EdgeInsets.symmetric(vertical: 8),
        decoration: BoxDecoration(
          color: sel ? MlColors.primary : const Color(0x00000000),
          borderRadius: BorderRadius.circular(14),
          border: isToday && !sel ? Border.all(color: MlColors.primary, width: 1.4) : null,
        ),
        child: Column(children: [
          Text(_days[d.weekday - 1], style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: sel ? MlColors.white : MlColors.textMuted, decoration: TextDecoration.none)),
          const SizedBox(height: 4),
          Text('${d.day}', style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: sel ? MlColors.white : MlColors.textPrimary, decoration: TextDecoration.none)),
          const SizedBox(height: 4),
          Container(width: 5, height: 5, decoration: BoxDecoration(color: dot ? (sel ? MlColors.white : MlColors.primary) : const Color(0x00000000), shape: BoxShape.circle)),
        ]),
      ),
    );
  }

  Widget _agendaRow(Map<String, dynamic> a) {
    final done = str(a['status']) == 'Completed';
    final type = str(a['type']);
    return Container(
      margin: const EdgeInsets.only(bottom: MlSpacing.sm),
      padding: const EdgeInsets.all(MlSpacing.md),
      decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.lg), border: Border.all(color: MlColors.border)),
      child: Row(children: [
        Column(children: [
          Text(timeLabel(a['dueAtUtc']), style: MlTypography.labelMedium.copyWith(color: done ? MlColors.textMuted : MlColors.primary)),
        ]),
        const SizedBox(width: MlSpacing.md),
        Container(width: 3, height: 38, decoration: BoxDecoration(color: done ? MlColors.success : MlColors.primary, borderRadius: BorderRadius.circular(2))),
        const SizedBox(width: MlSpacing.md),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              MlIcon(_icon(type), size: 18, color: MlColors.textMuted),
              const SizedBox(width: 6),
              Flexible(child: Text(str(a['subject']), maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary, decoration: done ? TextDecoration.lineThrough : TextDecoration.none))),
            ]),
            Text([type, if (str(a['priority']) == 'High') 'High priority'].join(' · '), style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
          ]),
        ),
        if (!done) MlButton(label: 'Done', size: MlButtonSize.small, variant: MlButtonVariant.secondary, onPressed: () => _complete(a)),
      ]),
    );
  }
}
