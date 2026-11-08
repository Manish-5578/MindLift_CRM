import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  bool _isLoading = false;
  MyDaySummaryDto? _summary;

  @override
  void initState() {
    super.initState();
    _loadMyDay();
  }

  Future<void> _loadMyDay() async {
    setState(() => _isLoading = true);
    try {
      final summary = await ref.read(activitiesServiceProvider).getMyDay();
      if (mounted) setState(() => _summary = summary);
    } catch (_) {
      // In offline or fallback mode, summary remains or loads from cache
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _completeTask(ActivityDto activity) async {
    try {
      await ref.read(activitiesServiceProvider).completeActivity(activity.id);
      MlToast.show(context, message: 'Activity marked complete!');
      await _loadMyDay();
    } catch (_) {
      MlToast.show(context, message: 'Saved to offline outbox.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final syncState = ref.watch(syncCoordinatorProvider);

    return MlScreen(
      title: 'My Day',
      subtitle: 'Field Agenda & Tasks',
      banner: MlConnectionBanner(
        state: syncState.connectionState,
        pendingCount: syncState.pendingCount,
        onRetry: () => ref.read(syncCoordinatorProvider.notifier).syncPendingMutations(),
      ),
      actions: [
        Consumer(
          builder: (context, ref, _) {
            final unread = ref.watch(unreadNotificationCountProvider).value ?? 0;
            return Stack(
              clipBehavior: Clip.none,
              children: [
                MlIconButton(
                  label: 'Notifications',
                  icon: const Text('🔔', style: TextStyle(fontSize: 18)),
                  onPressed: () => context.push('/notifications'),
                ),
                if (unread > 0)
                  Positioned(
                    top: 6,
                    right: 6,
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
                      decoration: BoxDecoration(
                        color: MlColors.danger,
                        borderRadius: BorderRadius.circular(MlRadii.full),
                      ),
                      constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
                      alignment: Alignment.center,
                      child: Text(
                        unread > 99 ? '99+' : '$unread',
                        style: const TextStyle(fontSize: 9, fontWeight: FontWeight.w700, color: MlColors.white),
                      ),
                    ),
                  ),
              ],
            );
          },
        ),
        MlIconButton(
          label: 'Refresh Agenda',
          icon: Text('↻', style: TextStyle(fontSize: 20, color: MlColors.textPrimary)),
          onPressed: _loadMyDay,
        ),
      ],
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
                  subtitle: 'You have no scheduled calls or tasks due today.',
                  actionLabel: 'Refresh',
                  onAction: _loadMyDay,
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

