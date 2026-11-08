import 'package:flutter/widgets.dart';

enum MlPageTransitionType {
  slideHorizontal,
  slideVertical,
  fade,
}

/// Custom route transition builder that avoids platform-mimicking animations.
class MlPageTransition {
  MlPageTransition._();

  static Route<T> buildRoute<T>({
    required Widget page,
    RouteSettings? settings,
    MlPageTransitionType type = MlPageTransitionType.slideHorizontal,
    Duration duration = const Duration(milliseconds: 250),
  }) {
    return PageRouteBuilder<T>(
      settings: settings,
      pageBuilder: (context, animation, secondaryAnimation) => page,
      transitionDuration: duration,
      reverseTransitionDuration: duration,
      transitionsBuilder: (context, animation, secondaryAnimation, child) {
        return buildTransition(
          context: context,
          animation: animation,
          secondaryAnimation: secondaryAnimation,
          child: child,
          type: type,
        );
      },
    );
  }

  static Widget buildTransition({
    required BuildContext context,
    required Animation<double> animation,
    required Animation<double> secondaryAnimation,
    required Widget child,
    MlPageTransitionType type = MlPageTransitionType.slideHorizontal,
  }) {
    final curvedAnimation = CurvedAnimation(
      parent: animation,
      curve: Curves.easeOutCubic,
      reverseCurve: Curves.easeInCubic,
    );

    switch (type) {
      case MlPageTransitionType.fade:
        return FadeTransition(
          opacity: curvedAnimation,
          child: child,
        );

      case MlPageTransitionType.slideVertical:
        return SlideTransition(
          position: Tween<Offset>(
            begin: const Offset(0.0, 0.15),
            end: Offset.zero,
          ).animate(curvedAnimation),
          child: FadeTransition(
            opacity: curvedAnimation,
            child: child,
          ),
        );

      case MlPageTransitionType.slideHorizontal:
      default:
        final isRtl = Directionality.maybeOf(context) == TextDirection.rtl;
        final beginOffset = isRtl ? const Offset(-0.2, 0.0) : const Offset(0.2, 0.0);
        return SlideTransition(
          position: Tween<Offset>(
            begin: beginOffset,
            end: Offset.zero,
          ).animate(curvedAnimation),
          child: FadeTransition(
            opacity: curvedAnimation,
            child: child,
          ),
        );
    }
  }
}

