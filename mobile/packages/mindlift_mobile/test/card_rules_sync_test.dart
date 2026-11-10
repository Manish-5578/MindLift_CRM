import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_mobile/features/leads/presentation/card_rules.g.dart';

/// The card reading rules live in shared/card-rules.json and are generated into both the server and this app.
/// This test fails when the phone copy is out of date; the server has the same test against the same file.
/// Fix a failure by running: python scripts/gen-card-rules.py
void main() {
  late Map<String, dynamic> rules;

  setUpAll(() {
    var dir = Directory.current;
    while (!File('${dir.path}/shared/card-rules.json').existsSync()) {
      final parent = dir.parent;
      if (parent.path == dir.path) fail('shared/card-rules.json was not found above ${Directory.current.path}');
      dir = parent;
    }
    rules = jsonDecode(File('${dir.path}/shared/card-rules.json').readAsStringSync()) as Map<String, dynamic>;
  });

  List<String> list(String key) => (rules[key] as List).cast<String>();

  test('word lists match the shared file', () {
    expect(CardRules.strongAddressTokens, list('strongAddressTokens'));
    expect(CardRules.weakAddressTokens, list('weakAddressTokens'));
    expect(CardRules.placeNames, list('placeNames'));
    expect(CardRules.addressLabels, list('addressLabels'));
    expect(CardRules.otherLabels, list('otherLabels'));
    expect(CardRules.honorifics, list('honorifics'));
    expect(CardRules.titleKeywords, list('titleKeywords'));
    expect(CardRules.companyKeywords, list('companyKeywords'));
  });

  test('patterns match the shared file', () {
    final patterns = rules['patterns'] as Map<String, dynamic>;
    expect(CardRules.noisePattern, patterns['noise']);
    expect(CardRules.foundedPattern, patterns['founded']);
    expect(CardRules.socialSitePattern, patterns['socialSite']);
  });

  test('every pattern compiles in Dart', () {
    for (final p in [CardRules.noisePattern, CardRules.foundedPattern, CardRules.socialSitePattern]) {
      expect(() => RegExp(p, caseSensitive: false), returnsNormally);
    }
  });
}
