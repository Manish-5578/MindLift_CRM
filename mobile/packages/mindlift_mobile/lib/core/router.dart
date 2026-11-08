import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../features/auth/presentation/login_screen.dart';
import '../features/home/presentation/home_screen.dart';
import '../features/leads/presentation/leads_screen.dart';
import '../features/leads/presentation/lead_detail_screen.dart';
import '../features/activities/presentation/activities_screen.dart';
import '../features/deals/presentation/deals_screen.dart';
import '../features/quick_add/presentation/quick_add_sheet.dart';
import '../features/notifications/presentation/notifications_screen.dart';
import '../features/more/presentation/more_screen.dart';
import 'network_provider.dart';

final routerProvider = Provider<GoRouter>((ref) {
  final authState = ref.watch(authNotifierProvider);

  return GoRouter(
    initialLocation: '/home',
    redirect: (context, state) {
      final isLoggingIn = state.matchedLocation == '/login';
      if (!authState.isAuthenticated) {
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
            if (loc.startsWith('/deals')) return 2;
            if (loc.startsWith('/activities')) return 3;
            if (loc.startsWith('/more')) return 4;
            return 0;
          }

          final currentIndex = calculateIndex(state.matchedLocation);

          return Column(
            children: [
              Expanded(child: child),
              MlBottomNavigation(
                currentIndex: currentIndex > 3 ? 3 : currentIndex,
                onTap: (index) {
                  switch (index) {
                    case 0:
                      context.go('/home');
                      break;
                    case 1:
                      context.go('/leads');
                      break;
                    case 2:
                      context.go('/deals');
                      break;
                    case 3:
                      context.go('/activities');
                      break;
                  }
                },
                onQuickAdd: () => QuickAddSheet.show(context),
                destinations: const [
                  MlNavigationDestination(
                    label: 'My Day',
                    icon: Text('📅', style: TextStyle(fontSize: 20)),
                  ),
                  MlNavigationDestination(
                    label: 'Leads',
                    icon: Text('👥', style: TextStyle(fontSize: 20)),
                  ),
                  MlNavigationDestination(
                    label: 'Deals',
                    icon: Text('💼', style: TextStyle(fontSize: 20)),
                  ),
                  MlNavigationDestination(
                    label: 'Activity',
                    icon: Text('⚡', style: TextStyle(fontSize: 20)),
                  ),
                ],
              ),
            ],
          );
        },
        routes: [
          GoRoute(
            path: '/home',
            builder: (context, state) => const HomeScreen(),
          ),
          GoRoute(
            path: '/leads',
            builder: (context, state) => const LeadsScreen(),
          ),
          GoRoute(
            path: '/deals',
            builder: (context, state) => const DealsScreen(),
          ),
          GoRoute(
            path: '/activities',
            builder: (context, state) => const ActivitiesScreen(),
          ),
          GoRoute(
            path: '/more',
            builder: (context, state) => const MoreScreen(),
          ),
        ],
      ),
      GoRoute(
        path: '/leads/:id',
        builder: (context, state) {
          final id = state.pathParameters['id'] ?? '';
          return LeadDetailScreen(leadId: id);
        },
      ),
      GoRoute(
        path: '/notifications',
        builder: (context, state) => const NotificationsScreen(),
      ),
    ],
  );
});

