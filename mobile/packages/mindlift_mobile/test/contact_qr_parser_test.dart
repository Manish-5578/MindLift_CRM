import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_mobile/features/leads/presentation/contact_qr_parser.dart';

void main() {
  group('vCard', () {
    test('reads a full 3.0 card', () {
      const raw = 'BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Omar Khalid\r\nN:Khalid;Omar;;;\r\nORG:Gulf Star Trading LLC;Sales\r\nTITLE:General Manager\r\n'
          'TEL;TYPE=WORK,VOICE:+971 4 555 0101\r\nTEL;TYPE=CELL:+971 50 123 4567\r\nEMAIL;TYPE=INTERNET:omar@gulfstar.ae\r\nURL:https://www.gulfstar.ae\r\n'
          'ADR;TYPE=WORK:;;Office 12\\, Tower B;Dubai;;00000;UAE\r\nEND:VCARD';
      final c = ContactQrParser.parse(raw)!;

      expect(c.name, 'Omar Khalid');
      expect(c.company, 'Gulf Star Trading LLC');
      expect(c.title, 'General Manager');
      expect(c.phone, '+971 50 123 4567', reason: 'the mobile number is preferred');
      expect(c.email, 'omar@gulfstar.ae');
      expect(c.website, 'https://www.gulfstar.ae');
      expect(c.address, 'Office 12, Tower B, Dubai, 00000, UAE');
    });

    test('builds the name from N when there is no FN', () {
      final c = ContactQrParser.parse('BEGIN:VCARD\nVERSION:2.1\nN:Nasser;Sara;Ali\nEMAIL:sara@nasser.com\nEND:VCARD')!;
      expect(c.name, 'Sara Ali Nasser');
    });

    test('joins folded lines and reads grouped properties and Arabic text', () {
      const raw = 'BEGIN:VCARD\nVERSION:3.0\nFN:محمد\n الخالد\nitem1.TEL:+971501234567\nORG:شركة النجمة\nEND:VCARD';
      final c = ContactQrParser.parse(raw)!;

      expect(c.name, 'محمدالخالد');
      expect(c.phone, '+971501234567');
      expect(c.company, 'شركة النجمة');
    });

    test('a card with no name, company, phone or email is ignored', () {
      expect(ContactQrParser.parse('BEGIN:VCARD\nVERSION:3.0\nNOTE:hello\nEND:VCARD'), isNull);
    });

    test('text for the duplicate lookup has one detail per line', () {
      final c = ContactQrParser.parse('BEGIN:VCARD\nFN:Omar Khalid\nEMAIL:omar@gulfstar.ae\nEND:VCARD')!;
      expect(c.asText, 'Omar Khalid\nomar@gulfstar.ae');
    });
  });

  group('MECARD', () {
    test('reads a card and turns Last,First round', () {
      final c = ContactQrParser.parse('MECARD:N:Khalid,Omar;ORG:Gulf Star;TEL:+971501234567;EMAIL:omar@gulfstar.ae;URL:www.gulfstar.ae;ADR:Tower B\\, Dubai;;')!;

      expect(c.name, 'Omar Khalid');
      expect(c.company, 'Gulf Star');
      expect(c.phone, '+971501234567');
      expect(c.email, 'omar@gulfstar.ae');
      expect(c.website, 'www.gulfstar.ae');
      expect(c.address, 'Tower B, Dubai');
    });
  });

  group('other codes', () {
    test('a web link, plain text, empty and null are not contacts', () {
      expect(ContactQrParser.parse('https://example.com/menu'), isNull);
      expect(ContactQrParser.parse('hello'), isNull);
      expect(ContactQrParser.parse('   '), isNull);
      expect(ContactQrParser.parse(null), isNull);
    });
  });
}
