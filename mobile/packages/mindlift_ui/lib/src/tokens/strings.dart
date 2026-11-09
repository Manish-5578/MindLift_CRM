/// Hook for translating the app's own text. The app sets [translate] when the language changes; every primitive
/// (buttons, headers, sheets, toasts, fields) runs its text through [MlStrings.t], so screens do not need to know.
class MlStrings {
  const MlStrings._();

  static String Function(String) translate = _same;

  static String _same(String s) => s;

  static String t(String s) => translate(s);

  static String? tn(String? s) => s == null ? null : translate(s);
}

extension MlTranslate on String {
  /// This text in the current language (English text is the key; missing entries stay in English).
  String get tr => MlStrings.t(this);
}
