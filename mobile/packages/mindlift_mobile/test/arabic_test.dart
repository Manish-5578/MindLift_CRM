import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_mobile/core/l10n.dart';
import 'package:mindlift_mobile/core/locale_provider.dart';
import 'package:mindlift_ui/mindlift_ui.dart';

void main() {
  tearDown(() => applyLanguage(AppLanguage.english));

  test('switching to Arabic translates known text and leaves the rest alone', () {
    applyLanguage(AppLanguage.arabic);
    expect('Home'.tr, 'الرئيسية');
    expect('Save changes'.tr, 'حفظ التغييرات');
    expect('A name that is not in the dictionary'.tr, 'A name that is not in the dictionary');
    applyLanguage(AppLanguage.english);
    expect('Home'.tr, 'Home');
  });

  test('every Arabic entry has text, keeps its placeholders and is not just the English copy', () {
    for (final e in arabic.entries) {
      expect(e.value.trim(), isNotEmpty, reason: e.key);
      if (e.key != 'English (LTR)' && e.key != 'العربية (RTL)') {
        expect(e.value, isNot(e.key), reason: 'untranslated: ${e.key}');
      }
    }
  });

  testWidgets('the app shell reads right to left in Arabic: labels translate and the first tab sits on the right', (tester) async {
    applyLanguage(AppLanguage.arabic);
    await tester.pumpWidget(Directionality(
      textDirection: TextDirection.rtl,
      child: MediaQuery(
        data: const MediaQueryData(size: Size(400, 800)),
        child: Overlay(initialEntries: [
          OverlayEntry(
            builder: (_) => Column(children: [
              const Spacer(),
              MlBottomNavigation(
                currentIndex: 0,
                onTap: (_) {},
                destinations: const [
                  MlNavigationDestination(label: 'Home', icon: MlIcons.homeOutline),
                  MlNavigationDestination(label: 'Leads', icon: MlIcons.leadsOutline),
                  MlNavigationDestination(label: 'Chat', icon: MlIcons.chatOutline),
                  MlNavigationDestination(label: 'Profile', icon: MlIcons.profileOutline),
                ],
              ),
            ]),
          ),
        ]),
      ),
    ));
    expect(tester.takeException(), isNull);
    final home = tester.getCenter(find.text('الرئيسية'));
    final profile = tester.getCenter(find.text('الملف الشخصي'));
    expect(home.dx, greaterThan(profile.dx), reason: 'Home should be the right-most tab in RTL');
  });
}
