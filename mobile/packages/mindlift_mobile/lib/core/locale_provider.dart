import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

enum AppLanguage {
  english(Locale('en'), TextDirection.ltr, 'English'),
  arabic(Locale('ar'), TextDirection.rtl, 'العربية');

  const AppLanguage(this.locale, this.direction, this.displayName);
  final Locale locale;
  final TextDirection direction;
  final String displayName;
}

class LocaleNotifier extends StateNotifier<AppLanguage> {
  LocaleNotifier() : super(AppLanguage.english);

  void setLanguage(AppLanguage language) {
    state = language;
  }

  void toggleLanguage() {
    state = state == AppLanguage.english ? AppLanguage.arabic : AppLanguage.english;
  }
}

final localeProvider = StateNotifierProvider<LocaleNotifier, AppLanguage>((ref) {
  return LocaleNotifier();
});

