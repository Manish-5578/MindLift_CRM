import 'package:test/test.dart';
import 'package:mindlift_api/mindlift_api.dart';

void main() {
  group('Auth Models', () {
    test('LoginRequest serialization', () {
      const req = LoginRequest(email: 'user@test.com', password: 'secretpassword');
      final json = req.toJson();
      expect(json['email'], equals('user@test.com'));
      expect(json['password'], equals('secretpassword'));
      expect(json['rememberMe'], isTrue);
    });

    test('TokenResponse deserialization', () {
      final json = {
        'accessToken': 'abc.123.jwt',
        'refreshToken': 'rt_456',
        'expiresIn': 7200,
        'role': 'Agent',
        'fullName': 'John Doe',
      };
      final res = TokenResponse.fromJson(json);
      expect(res.accessToken, equals('abc.123.jwt'));
      expect(res.refreshToken, equals('rt_456'));
      expect(res.expiresIn, equals(7200));
      expect(res.role, equals('Agent'));
      expect(res.fullName, equals('John Doe'));
    });
  });

  group('Lead and Activity Models', () {
    test('LeadDto deserialization with notesCount and attribution', () {
      final json = {
        'id': 'lead-001',
        'name': 'Acme Corp Lead',
        'company': 'Acme Inc',
        'status': 'Qualified',
        'createdBy': 'Alice Smith',
        'notesCount': 5,
        'version': 2,
      };
      final lead = LeadDto.fromJson(json);
      expect(lead.id, equals('lead-001'));
      expect(lead.name, equals('Acme Corp Lead'));
      expect(lead.status, equals('Qualified'));
      expect(lead.createdBy, equals('Alice Smith'));
      expect(lead.notesCount, equals(5));
      expect(lead.version, equals(2));
    });

    test('ActivityDto open task deserialization', () {
      final json = {
        'id': 'act-001',
        'type': 'Task',
        'subject': 'Follow up with client',
        'status': 'Open',
        'priority': 'High',
        'durationMinutes': 30,
      };
      final act = ActivityDto.fromJson(json);
      expect(act.type, equals('Task'));
      expect(act.status, equals('Open'));
      expect(act.priority, equals('High'));
      expect(act.durationMinutes, equals(30));
    });
  });

  group('Mobile Device Contracts', () {
    test('RegisterMobileDeviceRequest serialization', () {
      const req = RegisterMobileDeviceRequest(
        installationId: 'inst-1234',
        platform: 'android',
        deviceToken: 'fcm-token-test',
        locale: 'en',
        timeZoneId: 'Asia/Dubai',
      );
      final json = req.toJson();
      expect(json['installationId'], equals('inst-1234'));
      expect(json['platform'], equals('android'));
      expect(json['deviceToken'], equals('fcm-token-test'));
      expect(json['locale'], equals('en'));
      expect(json['timeZoneId'], equals('Asia/Dubai'));
    });
  });

  group('Offline Mutation Log and Sync Models', () {
    test('OfflineBatchSync serialization and deserialization', () {
      final mutation = OfflineClientMutationDto(
        clientMutationId: 'mut-100',
        entityType: 'Lead',
        operation: 'Create',
        clientTempId: 'temp-1',
        payload: {'name': 'New Lead Offline'},
      );
      final req = OfflineBatchSyncRequest(
        batchId: 'batch-999',
        syncCursor: 'cursor-123',
        mutations: [mutation],
      );
      final json = req.toJson();
      expect(json['batchId'], equals('batch-999'));
      expect(json['syncCursor'], equals('cursor-123'));
      expect((json['mutations'] as List).length, equals(1));
    });
  });
}

