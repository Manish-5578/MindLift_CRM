import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../leads/presentation/lead_actions.dart';
import '../../../core/offline_store.dart';
import '../../../core/top_bar_actions.dart';

class ActivitiesScreen extends ConsumerStatefulWidget {
  const ActivitiesScreen({super.key});

  @override
  ConsumerState<ActivitiesScreen> createState() => _ActivitiesScreenState();
}

class _ActivitiesScreenState extends ConsumerState<ActivitiesScreen> {
  String _selectedType = 'All';
  bool _isLoading = false;
  List<ActivityDto> _activities = [];

  static const _types = ['All', 'Task', 'Call', 'Meeting'];

  @override
  void initState() {
    super.initState();
    _loadActivities();
  }

  Future<void> _loadActivities() async {
    setState(() => _isLoading = true);
    try {
      final list = await ref.read(activitiesServiceProvider).listActivities(
            type: _selectedType == 'All' ? null : _selectedType,
          );
      if (mounted) setState(() => _activities = list);
    } catch (_) {
      // In offline or fallback mode
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _completeActivity(ActivityDto act) async {
    try {
      final sent = await sendOrQueue(
        ref,
        send: () => ref.read(activitiesServiceProvider).completeActivity(act.id),
        method: 'POST',
        path: '/api/v1/activities/${act.id}/complete',
        body: <String, dynamic>{},
        label: 'Complete ${act.subject}',
      );
      if (!mounted) return;
      MlToast.show(context, message: sent ? '${act.type} marked complete!' : 'Saved on this phone. It will sync when you are back online.');
      if (sent) await _loadActivities();
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not complete this activity.'), type: MlToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: 'Activities',
      subtitle: '${_activities.length} Scheduled Interactions',
      actions: topBarActions(),
      body: Column(
        children: [
          // Filter pills
          Container(
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: MlSpacing.sm),
            child: Row(
              children: _types.map((type) {
                final isSelected = type == _selectedType;
                return Padding(
                  padding: const EdgeInsetsDirectional.only(end: MlSpacing.xs),
                  child: GestureDetector(
                    onTap: () {
                      setState(() => _selectedType = type);
                      _loadActivities();
                    },
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                      decoration: BoxDecoration(
                        color: isSelected ? MlColors.primary : MlColors.surface,
                        borderRadius: BorderRadius.circular(MlRadii.full),
                        border: Border.all(
                          color: isSelected ? MlColors.primary : MlColors.border,
                        ),
                      ),
                      child: Text(
                        type,
                        style: MlTypography.caption.copyWith(
                          color: isSelected ? MlColors.white : MlColors.textMuted,
                          fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
                        ),
                      ),
                    ),
                  ),
                );
              }).toList(),
            ),
          ),

          // Activities List
          Expanded(
            child: MlPullToRefresh(
              onRefresh: _loadActivities,
              child: _isLoading && _activities.isEmpty
                  ? ListView.builder(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      itemCount: 4,
                      itemBuilder: (_, __) => const Padding(
                        padding: EdgeInsets.only(bottom: MlSpacing.sm),
                        child: MlSkeleton(height: 72),
                      ),
                    )
                  : _activities.isEmpty
                      ? MlEmptyState(
                          title: 'No activities found',
          icon: MlIcon(MlIcons.checkCircle, size: 30, color: MlColors.primary),
                          subtitle: 'You are all caught up on scheduled follow-ups.',
                        )
                      : ListView.builder(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(MlSpacing.md),
                          itemCount: _activities.length,
                          itemBuilder: (context, index) {
                            final act = _activities[index];
                            final isDone = act.status == 'Completed';

                            return Padding(
                              padding: const EdgeInsets.only(bottom: MlSpacing.sm),
                              child: MlRecordCard(
                                title: act.subject,
                                subtitle: '${act.type} • ${act.assignedToName ?? 'Field Agent'}',
                                statusChip: MlStatusChip(
                                  label: act.status,
                                  state: isDone
                                      ? MlStatusState.success
                                      : act.priority == 'High'
                                          ? MlStatusState.danger
                                          : MlStatusState.warning,
                                ),
                                trailing: !isDone
                                    ? MlButton(
                                        label: 'Complete',
                                        onPressed: () => _completeActivity(act),
                                        variant: MlButtonVariant.secondary,
                                      )
                                    : null,
                              ),
                            );
                          },
                        ),
            ),
          ),
        ],
      ),
    );
  }
}

