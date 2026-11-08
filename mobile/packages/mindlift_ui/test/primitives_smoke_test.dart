import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_ui/mindlift_ui.dart';

Widget _host(Widget child) => Directionality(
      textDirection: TextDirection.ltr,
      child: MediaQuery(
        data: const MediaQueryData(size: Size(400, 800)),
        child: Overlay(initialEntries: [OverlayEntry(builder: (_) => child)]),
      ),
    );

void main() {
  testWidgets('bottom navigation switches tab and shows the quick-add button', (tester) async {
    var index = 0;
    var added = false;
    await tester.pumpWidget(_host(StatefulBuilder(
      builder: (context, set) => MlBottomNavigation(
        currentIndex: index,
        onTap: (i) => set(() => index = i),
        onQuickAdd: () => added = true,
        destinations: const [
          MlNavigationDestination(label: 'Home', icon: MlIcons.homeOutline, activeIcon: MlIcons.home),
          MlNavigationDestination(label: 'Leads', icon: MlIcons.leadsOutline, activeIcon: MlIcons.leads),
          MlNavigationDestination(label: 'Chat', icon: MlIcons.chatOutline, activeIcon: MlIcons.chat),
          MlNavigationDestination(label: 'Profile', icon: MlIcons.profileOutline, activeIcon: MlIcons.profile, badgeCount: 3),
        ],
      ),
    )));
    await tester.tap(find.text('Chat'));
    await tester.pumpAndSettle();
    expect(index, 2);
    await tester.tap(find.bySemanticsLabel('Quick add'));
    expect(added, isTrue);
    expect(find.text('3'), findsOneWidget);
  });

  testWidgets('button taps, shows a spinner while loading, and a text field takes input', (tester) async {
    var taps = 0;
    final c = TextEditingController();
    await tester.pumpWidget(_host(Column(children: [
      MlButton(label: 'Save', onPressed: () => taps++),
      const MlButton(label: 'Busy', isLoading: true, onPressed: null),
      MlTextField(controller: c, placeholder: 'Type'),
    ])));
    await tester.tap(find.text('Save'));
    await tester.pump(const Duration(milliseconds: 200));
    expect(taps, 1);
    expect(find.byType(MlSpinner), findsOneWidget);
    await tester.tap(find.byType(EditableText));
    await tester.enterText(find.byType(EditableText), 'hello');
    expect(c.text, 'hello');
  });
}
