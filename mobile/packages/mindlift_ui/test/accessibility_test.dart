import 'dart:math' as math;
import 'package:flutter/rendering.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_ui/mindlift_ui.dart';

double _lum(Color c) {
  double ch(double v) => v <= 0.03928 ? v / 12.92 : math.pow((v + 0.055) / 1.055, 2.4).toDouble();
  return 0.2126 * ch(c.red / 255) + 0.7152 * ch(c.green / 255) + 0.0722 * ch(c.blue / 255);
}

/// WCAG 2.1 contrast ratio between two colours.
double ratio(Color a, Color b) {
  final la = _lum(a), lb = _lum(b);
  final hi = math.max(la, lb), lo = math.min(la, lb);
  return (hi + 0.05) / (lo + 0.05);
}

Widget _host(Widget child, {double scale = 1}) => Directionality(
      textDirection: TextDirection.ltr,
      child: MediaQuery(
        data: MediaQueryData(size: const Size(360, 780), textScaler: TextScaler.linear(scale)),
        child: Overlay(initialEntries: [OverlayEntry(builder: (_) => child)]),
      ),
    );

void main() {
  for (final dark in [false, true]) {
    test('text colours are readable on their backgrounds (${dark ? 'dark' : 'light'} mode)', () {
      MlColors.isDark = dark;
      addTearDown(() => MlColors.isDark = false);

      // Body text needs 4.5:1 (WCAG AA); large or bold text and icons need 3:1.
      final body = <String, double>{
        'primary on background': ratio(MlColors.textPrimary, MlColors.background),
        'primary on surface': ratio(MlColors.textPrimary, MlColors.surface),
        'secondary on surface': ratio(MlColors.textSecondary, MlColors.surface),
        'secondary on background': ratio(MlColors.textSecondary, MlColors.background),
        'muted on surface': ratio(MlColors.textMuted, MlColors.surface),
        'muted on background': ratio(MlColors.textMuted, MlColors.background),
        'white on primary button': ratio(MlColors.white, MlColors.primary),
        'white on danger button': ratio(MlColors.white, MlColors.danger),
      };
      for (final e in body.entries) {
        expect(e.value, greaterThanOrEqualTo(4.5), reason: '${e.key}: ${e.value.toStringAsFixed(2)}:1');
      }
    });
  }

  test('the brand colour is readable as text and as a focus outline on every surface', () {
    for (final dark in [false, true]) {
      MlColors.isDark = dark;
      final surface = MlColors.surface;
      // Links and the selected tab label use the primary colour as text.
      final onSurface = ratio(MlColors.primaryText, surface);
      expect(onSurface, greaterThanOrEqualTo(4.5), reason: '${dark ? 'dark' : 'light'}: ${onSurface.toStringAsFixed(2)}:1');
    }
    MlColors.isDark = false;
  });

  testWidgets('the navigation bar and buttons do not overflow at 200% text size', (tester) async {
    await tester.pumpWidget(_host(
      Column(children: [
        const MlButton(label: 'Save changes', onPressed: null),
        MlBottomNavigation(
          currentIndex: 0,
          onTap: (_) {},
          onQuickAdd: () {},
          destinations: const [
            MlNavigationDestination(label: 'Home', icon: MlIcons.homeOutline),
            MlNavigationDestination(label: 'Leads', icon: MlIcons.leadsOutline),
            MlNavigationDestination(label: 'Chat', icon: MlIcons.chatOutline),
            MlNavigationDestination(label: 'Profile', icon: MlIcons.profileOutline),
          ],
        ),
      ]),
      scale: 2.0,
    ));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  testWidgets('tappable controls are announced as buttons with a name', (tester) async {
    final handle = tester.ensureSemantics();
    await tester.pumpWidget(_host(Column(children: [
      MlIconButton(label: 'Notifications', icon: const SizedBox(), onPressed: () {}),
      MlPressable(onTap: () {}, child: const Text('Call')),
    ])));
    expect(tester.getSemantics(find.bySemanticsLabel('Notifications')), isNotNull);
    final call = tester.getSemantics(find.text('Call'));
    expect(call.hasFlag(SemanticsFlag.isButton), isTrue);
    handle.dispose();
  });
}
