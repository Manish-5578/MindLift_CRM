import 'package:flutter/widgets.dart';
import 'colors.dart';

/// Theme container and inherited widget for MindLift UI.
class MlTheme extends InheritedWidget {
  const MlTheme({
    super.key,
    required super.child,
    this.isDark = false,
    this.textDirection = TextDirection.ltr,
  });

  final bool isDark;
  final TextDirection textDirection;

  static MlTheme of(BuildContext context) {
    final theme = context.dependOnInheritedWidgetOfExactType<MlTheme>();
    return theme ?? const MlTheme(child: SizedBox());
  }

  Color get backgroundColor => MlColors.background;
  Color get surfaceColor => MlColors.surface;
  Color get primaryColor => MlColors.primary;
  Color get borderColor => MlColors.border;

  @override
  bool updateShouldNotify(MlTheme oldWidget) =>
      isDark != oldWidget.isDark || textDirection != oldWidget.textDirection;
}

