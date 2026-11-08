import 'package:flutter/widgets.dart';

/// There is no Scaffold in this app, so nothing lifts content above the on-screen keyboard.
/// This pads the whole app by the keyboard height (so inputs and send buttons stay visible) and tells
/// descendants when the keyboard is up so they can hide the bottom navigation.
class KeyboardInset extends StatelessWidget {
  const KeyboardInset({super.key, required this.child});
  final Widget child;

  static bool isVisible(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<_KeyboardVisible>()?.visible ?? false;

  @override
  Widget build(BuildContext context) {
    final mq = MediaQuery.of(context);
    final inset = mq.viewInsets.bottom;
    return _KeyboardVisible(
      visible: inset > 0,
      child: MediaQuery(
        data: mq.removeViewInsets(removeBottom: true),
        child: Padding(padding: EdgeInsets.only(bottom: inset), child: child),
      ),
    );
  }
}

class _KeyboardVisible extends InheritedWidget {
  const _KeyboardVisible({required this.visible, required super.child});
  final bool visible;

  @override
  bool updateShouldNotify(_KeyboardVisible old) => visible != old.visible;
}
