/// A contact read from a QR code on a business card (a vCard or a MECARD).
class ScannedContact {
  const ScannedContact({this.name, this.company, this.title, this.phone, this.email, this.website, this.address});

  final String? name;
  final String? company;
  final String? title;
  final String? phone;
  final String? email;
  final String? website;
  final String? address;

  /// True when the code carried something worth filling in. A code with no name, company, phone or email is ignored.
  bool get isUseful => [name, company, phone, email].any((v) => v != null && v.trim().isNotEmpty);

  /// The details as card text, one per line. Used to ask the server whether this person is already in the CRM.
  String get asText => [name, title, company, phone, email, website, address].whereType<String>().where((v) => v.trim().isNotEmpty).join('\n');
}

/// Reads the text of a QR code and, when it is a contact card, returns the contact. Anything else (a web link, plain text) gives null.
/// Understands vCard 2.1, 3.0 and 4.0 and the MECARD format that many card apps print.
class ContactQrParser {
  const ContactQrParser._();

  static ScannedContact? parse(String? raw) {
    final text = raw?.trim();
    if (text == null || text.isEmpty) return null;
    final ScannedContact? contact;
    if (RegExp(r'^BEGIN:VCARD', caseSensitive: false).hasMatch(text)) {
      contact = _vcard(text);
    } else if (text.toUpperCase().startsWith('MECARD:')) {
      contact = _mecard(text.substring(7));
    } else {
      return null;
    }
    return contact != null && contact.isUseful ? contact : null;
  }

  static String? _clean(String? value) {
    if (value == null) return null;
    final v = value.trim();
    return v.isEmpty ? null : v;
  }

  /// Turns the escapes a vCard uses (\n, \, \; \\) back into characters. A line break becomes a comma, which suits an address.
  static String _unescape(String value) =>
      value.replaceAll(RegExp(r'\\[nN]'), ', ').replaceAllMapped(RegExp(r'\\(.)'), (m) => m.group(1)!);

  static ScannedContact? _vcard(String text) {
    // A line that starts with a space or tab continues the one before it.
    final lines = text.replaceAll(RegExp(r'\r?\n[ \t]'), '').split(RegExp(r'\r?\n'));

    String? fn;
    String? n;
    String? org;
    String? title;
    String? email;
    String? url;
    String? adr;
    String? cell;
    String? firstPhone;

    for (final line in lines) {
      final colon = line.indexOf(':');
      if (colon <= 0) continue;
      final head = line.substring(0, colon).toUpperCase();
      final value = line.substring(colon + 1);
      var name = head.split(';').first;
      final dot = name.lastIndexOf('.'); // "item1.TEL" is a grouped property
      if (dot >= 0) name = name.substring(dot + 1);

      switch (name) {
        case 'FN':
          fn ??= _clean(_unescape(value));
        case 'N':
          if (n == null) {
            final p = value.split(';');
            String part(int i) => i < p.length ? _unescape(p[i]).trim() : '';
            final joined = [part(1), part(2), part(0)].where((s) => s.isNotEmpty).join(' ');
            n = _clean(joined);
          }
        case 'ORG':
          org ??= _clean(_unescape(value.split(';').firstWhere((s) => s.trim().isNotEmpty, orElse: () => '')));
        case 'TITLE':
          title ??= _clean(_unescape(value));
        case 'EMAIL':
          email ??= _clean(_unescape(value));
        case 'URL':
          url ??= _clean(_unescape(value));
        case 'ADR':
          if (adr == null) {
            final p = value.split(';');
            // pobox;extended;street;city;region;postcode;country
            adr = _clean([for (var i = 2; i < p.length && i <= 6; i++) _unescape(p[i]).trim()].where((s) => s.isNotEmpty).join(', '));
          }
        case 'TEL':
          final number = _clean(_unescape(value).replaceFirst(RegExp(r'^tel:', caseSensitive: false), ''));
          if (number == null) break;
          firstPhone ??= number;
          if (head.contains('CELL') || head.contains('MOBILE')) cell ??= number;
      }
    }

    return ScannedContact(
      name: fn ?? n,
      company: org,
      title: title,
      phone: cell ?? firstPhone,
      email: email,
      website: url,
      address: adr,
    );
  }

  static ScannedContact? _mecard(String body) {
    String? name;
    String? company;
    String? title;
    String? phone;
    String? email;
    String? url;
    String? adr;

    // Fields are KEY:value pairs separated by a semicolon; a backslash escapes a semicolon, colon, comma or backslash.
    for (final field in body.split(RegExp(r'(?<!\\);'))) {
      final colon = field.indexOf(':');
      if (colon <= 0) continue;
      final key = field.substring(0, colon).toUpperCase();
      final value = _clean(_unescape(field.substring(colon + 1)));
      if (value == null) continue;
      switch (key) {
        case 'N':
          // "Last,First" is written the other way round.
          final parts = value.split(',').map((s) => s.trim()).where((s) => s.isNotEmpty).toList();
          name ??= parts.length == 2 ? '${parts[1]} ${parts[0]}' : parts.join(' ');
        case 'ORG':
          company ??= value;
        case 'TITLE':
          title ??= value;
        case 'TEL':
          phone ??= value;
        case 'EMAIL':
          email ??= value;
        case 'URL':
          url ??= value;
        case 'ADR':
          adr ??= value.split(',').map((s) => s.trim()).where((s) => s.isNotEmpty).join(', ');
      }
    }

    return ScannedContact(name: name, company: company, title: title, phone: phone, email: email, website: url, address: adr);
  }
}
