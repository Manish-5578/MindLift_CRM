import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'keyboard_inset.dart';
import '../features/auth/presentation/login_screen.dart';
import '../features/home/presentation/home_screen.dart';
import '../features/leads/presentation/leads_screen.dart';
import '../features/leads/presentation/lead_detail_screen.dart';
import '../features/activities/presentation/activities_screen.dart';
import '../features/workspace/workspace_screens.dart';
import '../features/workspace/calendar_screen.dart';
import '../features/notifications/presentation/notification_settings_screen.dart';
import '../features/quick_add/presentation/quick_add_sheet.dart';
import '../features/notifications/presentation/notifications_screen.dart';
import '../features/more/presentation/more_screen.dart';
import '../features/profile/presentation/profile_screen.dart';
import '../features/chat/presentation/chat_screen.dart';
import 'network_provider.dart';
import 'offline_banner.dart';

/// Listenable that notifies GoRouter ONLY when authentication status changes,
/// preventing route teardowns and input loss when other auth fields (e.g. isLoading) change.
class AuthRouterListenable extends ChangeNotifier {
  AuthRouterListenable(Ref ref) {
    ref.listen<bool>(
      authNotifierProvider.select((state) => state.isAuthenticated),
      (prev, next) {
        if (prev != next) {
          notifyListeners();
        }
      },
    );
  }
}

final routerProvider = Provider<GoRouter>((ref) {
  final listenable = AuthRouterListenable(ref);

  return GoRouter(
    initialLocation: '/home',
    refreshListenable: listenable,
    redirect: (context, state) {
      final isAuthenticated = ref.read(authNotifierProvider).isAuthenticated;
      final isLoggingIn = state.matchedLocation == '/login';
      if (!isAuthenticated) {
        return isLoggingIn ? null : '/login';
      }
      if (isLoggingIn) {
        return '/home';
      }
      return null;
    },
    routes: [
      GoRoute(
        path: '/login',
        builder: (context, state) => const LoginScreen(),
      ),
      ShellRoute(
        builder: (context, state, child) {
          int calculateIndex(String loc) {
            if (loc.startsWith('/home')) return 0;
            if (loc.startsWith('/leads')) return 1;
            if (loc.startsWith('/chat')) return 2;
            if (loc.startsWith('/profile') || loc.startsWith('/more')) return 3;
            return 0;
          }

          final currentIndex = calculateIndex(state.matchedLocation);

          return Column(
            children: [
              const OfflineBanner(),
              Expanded(child: child),
              if (!KeyboardInset.isVisible(context))
              MlBottomNavigation(
                currentIndex: currentIndex,
                onTap: (index) {
                  switch (index) {
                    case 0:
                      context.go('/home');
                      break;
                    case 1:
                      context.go('/leads');
                      break;
                    case 2:
                      context.go('/chat');
                      break;
                    case 3:
                      context.go('/profile');
                      break;
                  }
                },
                onQuickAdd: () => QuickAddSheet.show(context),
                destinations: [
                  const MlNavigationDestination(label: 'Home', icon: MlIcons.homeOutline, activeIcon: MlIcons.home),
                  const MlNavigationDestination(label: 'Leads', icon: MlIcons.leadsOutline, activeIcon: MlIcons.leads),
                  const MlNavigationDestination(label: 'Chat', icon: MlIcons.chatOutline, activeIcon: MlIcons.chat),
                  const MlNavigationDestination(label: 'Profile', icon: MlIcons.profileOutline, activeIcon: MlIcons.profile),
                ],
              ),
            ],
          );
        },
        routes: [
          GoRoute(
            path: '/home',
            pageBuilder: (context, state) => _fade(state, const HomeScreen()),
          ),
          GoRoute(
            path: '/leads',
            pageBuilder: (context, state) => _fade(state, const LeadsScreen()),
            routes: [
              GoRoute(
                path: ':id',
                pageBuilder: (context, state) {
                  final leadId = state.pathParameters['id'] ?? '';
                  return _slide(state, LeadDetailScreen(leadId: leadId));
                },
              ),
            ],
          ),
          GoRoute(
            path: '/deals',
            pageBuilder: (context, state) => _fade(state, const DealsScreen()),
            routes: [GoRoute(path: ':id', pageBuilder: (context, state) => _slide(state, DealDetailScreen(id: state.pathParameters['id'] ?? '')))],
          ),
          GoRoute(
            path: '/contacts',
            pageBuilder: (context, state) => _fade(state, const ContactsScreen()),
            routes: [GoRoute(path: ':id', pageBuilder: (context, state) => _slide(state, ContactDetailScreen(id: state.pathParameters['id'] ?? '')))],
          ),
          GoRoute(
            path: '/accounts',
            pageBuilder: (context, state) => _fade(state, const AccountsScreen()),
            routes: [GoRoute(path: ':id', pageBuilder: (context, state) => _slide(state, AccountDetailScreen(id: state.pathParameters['id'] ?? '')))],
          ),
          GoRoute(
            path: '/calendar',
            pageBuilder: (context, state) => _fade(state, const CalendarScreen()),
          ),
          GoRoute(
            path: '/notification-settings',
            pageBuilder: (context, state) => _slide(state, const NotificationSettingsScreen()),
          ),
          GoRoute(
            path: '/activities',
            pageBuilder: (context, state) => _fade(state, const ActivitiesScreen()),
          ),
          GoRoute(
            path: '/more',
            pageBuilder: (context, state) => _fade(state, const MoreScreen()),
          ),
          GoRoute(
            path: '/notifications',
            pageBuilder: (context, state) => _fade(state, const NotificationsScreen()),
          ),
          GoRoute(
            path: '/chat',
            pageBuilder: (context, state) => _fade(state, const ChatScreen()),
          ),
          GoRoute(
            path: '/profile',
            pageBuilder: (context, state) => _fade(state, const ProfileScreen()),
          ),
        ],
      ),
    ],
  );
});


/// Switching tabs: a quick fade-through, so content never slides sideways between peers.
CustomTransitionPage<void> _fade(GoRouterState state, Widget child) => CustomTransitionPage<void>(
      key: state.pageKey,
      child: child,
      transitionDuration: const Duration(milliseconds: 220),
      reverseTransitionDuration: const Duration(milliseconds: 160),
      transitionsBuilder: (context, animation, secondary, child) => FadeTransition(
        opacity: CurvedAnimation(parent: animation, curve: Curves.easeOut),
        child: child,
      ),
    );

/// Opening a detail screen: shared-axis style slide and fade, with the same curve Material uses.
CustomTransitionPage<void> _slide(GoRouterState state, Widget child) => CustomTransitionPage<void>(
      key: state.pageKey,
      child: child,
      transitionDuration: const Duration(milliseconds: 320),
      reverseTransitionDuration: const Duration(milliseconds: 260),
      transitionsBuilder: (context, animation, secondary, child) => MlPageTransition.buildTransition(
        context: context,
        animation: animation,
        secondaryAnimation: secondary,
        child: child,
      ),
    );
