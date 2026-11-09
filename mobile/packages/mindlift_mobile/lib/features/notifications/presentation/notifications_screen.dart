import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  bool _isLoading = false;
  List<NotificationItemDto> _notifications = [];

  @override
  void initState() {
    super.initState();
    _loadNotifications();
  }

  Future<void> _loadNotifications() async {
    setState(() => _isLoading = true);
    try {
      final list = await ref.read(notificationsServiceProvider).listNotifications();
      if (mounted) setState(() => _notifications = list);
    } catch (_) {
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _markAllRead() async {
    try {
      await ref.read(notificationsServiceProvider).markAllAsRead();
      setState(() {
        _notifications = _notifications.map((n) {
          return NotificationItemDto(
            id: n.id,
            title: n.title,
            message: n.message,
            type: n.type,
            isRead: true,
            createdAtUtc: n.createdAtUtc,
            targetRoute: n.targetRoute,
            entityId: n.entityId,
          );
        }).toList();
      });
      MlToast.show(context, message: 'All notifications marked as read.');
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: 'Notifications',
      subtitle: '${_notifications.where((n) => !n.isRead).length} Unread',
      leading: MlIconButton(label: 'Back', icon: const MlIcon(MlIcons.back, size: 24), onPressed: () => context.canPop() ? context.pop() : context.go('/home')),
      actions: [
        MlButton(
          label: 'Mark all read',
          size: MlButtonSize.small,
          onPressed: _notifications.isEmpty ? null : _markAllRead,
          variant: MlButtonVariant.ghost,
        ),
        MlIconButton(label: 'Notification settings', icon: const MlIcon(MlIcons.settings, size: 24), onPressed: () => context.push('/notification-settings')),
      ],
      body: MlPullToRefresh(
        onRefresh: _loadNotifications,
        child: _isLoading && _notifications.isEmpty
            ? ListView.builder(
                padding: const EdgeInsets.all(MlSpacing.md),
                itemCount: 4,
                itemBuilder: (_, __) => const Padding(
                  padding: EdgeInsets.only(bottom: MlSpacing.sm),
                  child: MlSkeleton(height: 72),
                ),
              )
            : _notifications.isEmpty
                ? MlEmptyState(
                    title: 'No notifications',
          icon: MlIcon(MlIcons.bell, size: 30, color: MlColors.primary),
                    subtitle: 'You are completely caught up on CRM updates.',
                  )
                : ListView.builder(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(MlSpacing.md),
                    itemCount: _notifications.length,
                    itemBuilder: (context, index) {
                      final item = _notifications[index];
                      return Padding(
                        padding: const EdgeInsets.only(bottom: MlSpacing.sm),
                        child: MlRecordCard(
                          title: item.title,
                          subtitle: item.message,
                          statusChip: MlStatusChip(
                            label: item.isRead ? 'Read' : 'New',
                            state: item.isRead ? MlStatusState.neutral : MlStatusState.primary,
                          ),
                          onTap: () async {
                            if (!item.isRead) {
                              try {
                                await ref.read(notificationsServiceProvider).markAsRead(item.id);
                                setState(() {
                                  _notifications[index] = NotificationItemDto(
                                    id: item.id,
                                    title: item.title,
                                    message: item.message,
                                    type: item.type,
                                    isRead: true,
                                    createdAtUtc: item.createdAtUtc,
                                    targetRoute: item.targetRoute,
                                    entityId: item.entityId,
                                    entityType: item.entityType,
                                  );
                                });
                              } catch (_) {}
                            }
                            if (item.targetRoute != null && item.targetRoute!.isNotEmpty) {
                              context.push(item.targetRoute!);
                            }
                          },
                        ),
                      );
                    },
                  ),
      ),
    );
  }
}

