import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_mobile/features/leads/presentation/card_line_classifier.dart';

void main() {
  group('isAddressText', () {
    const addresses = [
      'Landmark: Near ASC',
      'Near Forum Mall, Koramangala',
      'Plot 18, Sector 62, Noida 201301',
      'Address: 12 MG Road',
      'Bengaluru, Karnataka',
      '4th Main, 2nd Cross',
      'Bengaluru - 560 102',
      'No. 42, 3rd Cross, HSR Layout',
    ];
    for (final line in addresses) {
      test('"$line" is an address', () => expect(CardLineClassifier.isAddressText(line), isTrue));
    }

    const notAddresses = [
      'Rahul Sharma',
      'Delhi Kumar',
      'Park Min-jun',
      'Main Uddin',
      'Name: Rahul Sharma',
      'Mobile: +91 98765 43210',
      '+91 80 4123 9988',
    ];
    for (final line in notAddresses) {
      test('"$line" is not an address', () => expect(CardLineClassifier.isAddressText(line), isFalse));
    }

    test('a company name that contains a city stays a company', () {
      expect(CardLineClassifier.isAddressText('Dubai Gold Trading LLC', hasCompanyWord: true), isFalse);
    });

    test('a company line with a street is still an address', () {
      expect(CardLineClassifier.isAddressText('Acme Ltd, 12 MG Road', hasCompanyWord: true), isTrue);
    });
  });

  group('looksLikePhoneLine', () {
    test('a labelled or international number is a phone line', () {
      expect(CardLineClassifier.looksLikePhoneLine('Tel: 080 4123 9988'), isTrue);
      expect(CardLineClassifier.looksLikePhoneLine('+971 50 123 4567'), isTrue);
    });

    test('a PIN code line is not', () {
      expect(CardLineClassifier.looksLikePhoneLine('Bengaluru 560 001'), isFalse);
    });
  });

  group('isNoiseLine', () {
    test('social handles, certifications and footers are noise', () {
      for (final line in ['@gulfstar_ae', 'linkedin.com/in/omar-khalid', 'ISO 9001:2015', 'All rights reserved', 'Est. 1998']) {
        expect(CardLineClassifier.isNoiseLine(line), isTrue, reason: line);
      }
    });

    test('real card lines are not noise', () {
      for (final line in ['Omar Khalid', 'Gulf Star Trading LLC', 'Dubai Gold Trading LLC Est. 1998', 'Isolation Systems LLC']) {
        expect(CardLineClassifier.isNoiseLine(line), isFalse, reason: line);
      }
    });

    test('a social page is not the company website', () {
      expect(CardLineClassifier.isSocialSite('www.linkedin.com/in/omar'), isTrue);
      expect(CardLineClassifier.isSocialSite('www.gulfstar.ae'), isFalse);
    });
  });
}
