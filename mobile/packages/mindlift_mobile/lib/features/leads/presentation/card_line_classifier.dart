import 'card_rules.g.dart';

/// Decides whether a line read from a business card is part of an address. Mirrors the server's CardLineClassifier so the phone's offline
/// reading, the name guard and the server agree: a landmark, a street word, a PIN code or an "Address:" label makes a line an address, and an
/// address is never the contact person.
class CardLineClassifier {
  const CardLineClassifier._();

  static final strongAddressTokens = CardRules.strongAddressTokens.toSet();
  static final weakAddressTokens = CardRules.weakAddressTokens.toSet();
  static final placeTokens = CardRules.placeNames.toSet();
  static final addressLabels = CardRules.addressLabels.toSet();
  static final otherLabels = CardRules.otherLabels.toSet();
  static final pinRegex = RegExp(r'(?<![\d+])[1-9]\d{2}\s?\d{3}(?!\d)');

  static final _noise = RegExp(CardRules.noisePattern, caseSensitive: false);
  static final _founded = RegExp(CardRules.foundedPattern, caseSensitive: false);
  static final _socialSite = RegExp(CardRules.socialSitePattern, caseSensitive: false);

  /// True for a line that is not contact data: a social handle or page, a certification mark (ISO 9001), a copyright footer or an "Est. 1998" tagline.
  /// Mirrors the server's CardLineClassifier.IsNoiseLine.
  static bool isNoiseLine(String line) {
    if (line.trim().isEmpty) return false;
    if (_noise.hasMatch(line)) return true;
    return _founded.hasMatch(line) && line.split(' ').where((w) => w.isNotEmpty).length <= 4;
  }

  /// True when the web address is a social media page rather than the company's own site.
  static bool isSocialSite(String site) => _socialSite.hasMatch(site.trim());

  static bool looksLikePhoneLine(String line) {
    final colon = line.indexOf(':');
    if (colon > 0 && colon <= 24 && otherLabels.contains(line.substring(0, colon).trim().toLowerCase())) return true;
    final t = line.trimLeft();
    return t.startsWith('+') || RegExp(r'^\(?\d{2,4}\)?[\s\-.]\d{3,5}[\s\-.]\d{3,5}').hasMatch(t);
  }

  /// True when the line is part of an address: a landmark or street word, a PIN code, an "Address:" label, or only place names.
  static bool isAddressText(String line, {bool hasCompanyWord = false}) {
    final trimmed = line.trim();
    if (trimmed.isEmpty) return false;

    final colon = trimmed.indexOf(':');
    if (colon > 0 && colon <= 24) {
      final label = trimmed.substring(0, colon).trim().replaceAll(RegExp(r'\.$'), '').toLowerCase();
      if (addressLabels.contains(label)) return true;
      if (otherLabels.contains(label)) return false;
    }

    final tokens = trimmed
        .toLowerCase()
        .split(RegExp(r'[\s,،;\-/\\|:()]+'))
        .where((t) => t.isNotEmpty)
        .toList();
    final hasDigit = RegExp(r'\d').hasMatch(trimmed);
    final hasStrong = tokens.any(strongAddressTokens.contains);
    final hasPin = pinRegex.hasMatch(trimmed) && !looksLikePhoneLine(trimmed);
    if (hasStrong || hasPin) return true;
    if (hasCompanyWord) return false;

    final commas = RegExp(r'[,،]').allMatches(trimmed).length;
    if (tokens.any(weakAddressTokens.contains) && (hasDigit || commas > 0)) return true;

    final places = tokens.where(placeTokens.contains).length;
    final others = tokens.where((t) => !const {'and', '&', 'of', 'the', 'و'}.contains(t)).length;
    if (places > 0 && places == others) return true;
    if (places > 0 && (hasDigit || commas > 0)) return true;

    return hasDigit && commas >= 1 && tokens.length >= 3;
  }
}
