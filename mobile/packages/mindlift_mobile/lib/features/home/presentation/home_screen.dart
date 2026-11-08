import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../leads/presentation/lead_actions.dart';
import '../../../core/offline_store.dart';
import '../../../core/top_bar_actions.dart';

class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  bool _isLoading = false;
  MyDaySummaryDto? _summary;
  // Snapshot of my leads: total per status, and the newest few.
  Map<String, int> _leadCounts = const {};
  List<LeadRow> _latestLeads = const [];

  static Map<String, dynamic> _mine(List<String> statuses) => {
        'logic': 'and',
        'conditions': [
          {'field': 'owner_id', 'op': LeadView.opEq, 'value': '@me'},
          {'field': 'status', 'op': LeadView.opIn, 'value': statuses},
        ],
        'groups': <dynamic>[],
      };

  static const _tourKey = 'onboarding_tour_v1';

  /// One-time quick tour of the gestures people would not otherwise find.
  Future<void> _maybeShowTour() async {
    try {
      const store = FlutterSecureStorage();
      if (await store.read(key: _tourKey) != null) return;
      await store.write(key: _tourKey, value: '1');
      if (!mounted) return;
      MlSheet.show<void>(
        context: context,
        title: 'Quick tour',
        builder: (sheetContext) => Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            children: [
              _tip(MlIcons.refresh, 'Pull down to refresh', 'Drag any list down to load the latest.'),
              _tip(MlIcons.swap, 'Swipe a lead', 'Right to call, left to change its status.'),
              _tip(MlIcons.add, 'Quick add', 'The + button creates a lead, task, call or note, or scans a business card.'),
              _tip(MlIcons.chatOutline, 'Rich chat', 'Send photos, files and voice notes. Long-press a message to reply or react.'),
              _tip(MlIcons.themeAuto, 'Appearance', 'Tap the sun / moon icon (top right) to switch Auto, Light and Dark.'),
              const SizedBox(height: MlSpacing.md),
              MlButton(label: 'Got it', onPressed: () => Navigator.of(sheetContext).pop()),
            ],
          ),
        ),
      );
    } catch (_) {}
  }

  Widget _tip(IconData icon, String title, String body) => Padding(
        padding: const EdgeInsets.only(bottom: MlSpacing.md),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              width: 42,
              height: 42,
              decoration: BoxDecoration(color: MlColors.primary.withOpacity(0.12), borderRadius: BorderRadius.circular(12)),
              alignment: Alignment.center,
              child: MlIcon(icon, size: 22, color: MlColors.primary),
            ),
            const SizedBox(width: MlSpacing.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)),
                  const SizedBox(height: 2),
                  Text(body, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted)),
                ],
              ),
            ),
          ],
        ),
      );

  Future<void> _loadLeadSnapshot() async {
    try {
      final svc = ref.read(leadsServiceProvider);
      final results = await Future.wait([
        svc.queryPage(filter: _mine(['new']), pageSize: 1),
        svc.queryPage(filter: _mine(['working']), pageSize: 1),
        svc.queryPage(filter: _mine(['qualified']), pageSize: 1),
        svc.queryPage(filter: _mine(['new', 'working', 'qualified']), pageSize: 4),
      ]);
      if (!mounted) return;
      setState(() {
        _leadCounts = {'new': results[0].total, 'working': results[1].total, 'qualified': results[2].total};
        _latestLeads = results[3].items;
      });
      unawaited(ref.read(offlineStoreProvider).putCache('home-leads', {
        'counts': _leadCounts,
        'latest': _latestLeads.map((l) => l.toJson()).toList(),
      }));
    } catch (e) {
      if (isOfflineError(e)) {
        final saved = await ref.read(offlineStoreProvider).getCache('home-leads');
        if (mounted && saved != null && _leadCounts.isEmpty) {
          final m = Map<String, dynamic>.from(saved.data as Map);
          setState(() {
            _leadCounts = Map<String, int>.from((m['counts'] as Map).map((k, v) => MapEntry(k as String, (v as num).toInt())));
            _latestLeads = ((m['latest'] as List?) ?? const []).whereType<Map>().map((x) => LeadRow.fromJson(Map<String, dynamic>.from(x))).toList();
          });
        }
      }
    }
  }

  @override
  void initState() {
    super.initState();
    _loadMyDay();
    WidgetsBinding.instance.addPostFrameCallback((_) => _maybeShowTour());
  }

  Future<void> _loadMyDay() async {
    _loadLeadSnapshot();
    setState(() => _isLoading = true);
    try {
      final summary = await ref.read(activitiesServiceProvider).getMyDay();
      if (mounted) setState(() => _summary = summary);
      unawaited(ref.read(offlineStoreProvider).putCache('myday', {
        'overdueCount': summary.overdueCount,
        'dueTodayCount': summary.dueTodayCount,
        'completedTodayCount': summary.completedTodayCount,
        'activities': summary.activities.map((a) => a.toJson()).toList(),
      }));
    } catch (e) {
      if (isOfflineError(e)) {
        ref.read(outboxProvider.notifier).markOffline();
        final saved = await ref.read(offlineStoreProvider).getCache('myday');
        if (mounted && saved != null && _summary == null) {
          setState(() => _summary = MyDaySummaryDto.fromJson(Map<String, dynamic>.from(saved.data as Map)));
        }
      }
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _completeTask(ActivityDto activity) async {
    try {
      final sent = await sendOrQueue(
        ref,
        send: () => ref.read(activitiesServiceProvider).completeActivity(activity.id),
        method: 'POST',
        path: '/api/v1/activities/${activity.id}/complete',
        body: <String, dynamic>{},
        label: 'Complete ${activity.subject}',
      );
      if (!mounted) return;
      MlToast.show(context, message: sent ? 'Activity marked complete!' : 'Saved on this phone. It will sync when you are back online.');
      if (sent) await _loadMyDay();
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not complete this activity.'), type: MlToastType.error);
    }
  }

  static String _firstName(String? full) {
    final n = (full ?? '').trim();
    if (n.isEmpty) return 'there';
    return n.split(RegExp(r'\s+')).first;
  }

  @override
  Widget build(BuildContext context) {
    final syncState = ref.watch(syncCoordinatorProvider);

    return MlScreen(
      title: 'Hi, ${_firstName(ref.watch(authNotifierProvider).userFullName)}',
      subtitle: 'Field Agenda & Tasks',
      banner: MlConnectionBanner(
        state: syncState.connectionState,
        pendingCount: syncState.pendingCount,
        onRetry: () => ref.read(syncCoordinatorProvider.notifier).syncPendingMutations(),
      ),
      actions: topBarActions(),
      body: MlPullToRefresh(
        onRefresh: _loadMyDay,
        child: SingleChildScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              // Metrics Summary Row
              _buildMetricsRow(),
              const SizedBox(height: MlSpacing.lg),

              // My leads snapshot
              if (_leadCounts.isNotEmpty) ...[
                Row(
                  children: [
                    Expanded(child: Text('My leads', style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary))),
                    MlPressable(
                      onTap: () => context.go('/leads'),
                      child: Row(children: [
                        Text('View all', style: MlTypography.labelMedium.copyWith(color: MlColors.primary)),
                        const MlIcon(MlIcons.chevronRight, size: 20, color: MlColors.primary),
                      ]),
                    ),
                  ],
                ),
                const SizedBox(height: MlSpacing.sm),
                Row(
                  children: [
                    Expanded(child: _leadTile('New', _leadCounts['new'] ?? 0, MlColors.primary)),
                    const SizedBox(width: MlSpacing.sm),
                    Expanded(child: _leadTile('Contacted', _leadCounts['working'] ?? 0, MlColors.warning)),
                    const SizedBox(width: MlSpacing.sm),
                    Expanded(child: _leadTile('Qualified', _leadCounts['qualified'] ?? 0, MlColors.success)),
                  ],
                ),
                const SizedBox(height: MlSpacing.sm),
                for (final l in _latestLeads)
                  MlPressable(
                    scale: 0.98,
                    onTap: () => context.push('/leads/${l.id}'),
                    child: Container(
                      margin: const EdgeInsets.only(bottom: 6),
                      padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 10),
                      decoration: BoxDecoration(
                        color: MlColors.surface,
                        borderRadius: BorderRadius.circular(MlRadii.lg),
                        border: Border.all(color: MlColors.border),
                      ),
                      child: Row(
                        children: [
                          MlAvatar(name: l.title, size: MlAvatarSize.sm),
                          const SizedBox(width: MlSpacing.md),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(l.title, maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                                Text(l.subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                              ],
                            ),
                          ),
                          MlIcon(MlIcons.chevronRight, size: 20, color: MlColors.textMuted),
                        ],
                      ),
                    ),
                  ),
                const SizedBox(height: MlSpacing.lg),
              ],

              // Section Header
              Text(
                'Today\'s Priority Items',
                style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
              ),
              const SizedBox(height: MlSpacing.sm),

              // Activities List
              if (_isLoading && _summary == null)
                ...List.generate(
                  3,
                  (i) => const Padding(
                    padding: EdgeInsets.only(bottom: MlSpacing.sm),
                    child: MlSkeleton(height: 72),
                  ),
                )
              else if (_summary == null || _summary!.activities.isEmpty)
                MlEmptyState(
                  title: 'All caught up!',
          icon: MlIcon(MlIcons.checkCircle, size: 30, color: MlColors.primary),
                  subtitle: 'You have no scheduled calls or tasks due today.',
                )
              else
                for (final act in _summary!.activities)
                  Padding(
                    padding: const EdgeInsets.only(bottom: MlSpacing.sm),
                    child: MlRecordCard(
                      title: act.subject,
                      subtitle: act.description ?? '${act.type} • Due ${act.dueAtUtc?.hour ?? 9}:00',
                      statusChip: MlStatusChip(
                        label: act.status,
                        state: act.status == 'Completed'
                            ? MlStatusState.success
                            : act.priority == 'High'
                                ? MlStatusState.danger
                                : MlStatusState.warning,
                      ),
                      trailing: act.status != 'Completed'
                          ? MlButton(
                              label: 'Done',
                              onPressed: () => _completeTask(act),
                              variant: MlButtonVariant.secondary,
                            )
                          : null,
                    ),
                  ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildMetricsRow() {
    final overdue = _summary?.overdueCount ?? 0;
    final dueToday = _summary?.dueTodayCount ?? 0;
    final completed = _summary?.completedTodayCount ?? 0;

    return Row(
      children: [
        Expanded(
          child: _buildMetricTile('Overdue', overdue.toString(), MlColors.danger),
        ),
        const SizedBox(width: MlSpacing.sm),
        Expanded(
          child: _buildMetricTile('Due Today', dueToday.toString(), MlColors.warning),
        ),
        const SizedBox(width: MlSpacing.sm),
        Expanded(
          child: _buildMetricTile('Completed', completed.toString(), MlColors.success),
        ),
      ],
    );
  }

  Widget _leadTile(String label, int count, Color accent) {
    return MlPressable(
      onTap: () => context.go('/leads'),
      child: Container(
        padding: const EdgeInsets.all(MlSpacing.md),
        decoration: BoxDecoration(
          color: accent.withOpacity(0.10),
          borderRadius: BorderRadius.circular(MlRadii.lg),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('$count', style: MlTypography.headingMedium.copyWith(color: accent, fontWeight: FontWeight.w800)),
            const SizedBox(height: 2),
            Text(label, style: MlTypography.caption.copyWith(color: MlColors.textSecondary)),
          ],
        ),
      ),
    );
  }

  Widget _buildMetricTile(String label, String count, Color accent) {
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
          Text(
            count,
            style: MlTypography.headingMedium.copyWith(color: accent, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: MlTypography.caption.copyWith(color: MlColors.textMuted),
          ),
        ],
      ),
    );
  }
}

