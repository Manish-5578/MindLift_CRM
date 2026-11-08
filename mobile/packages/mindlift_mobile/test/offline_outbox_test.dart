import 'dart:convert';
import 'dart:io';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_mobile/core/auth_provider.dart';
import 'package:mindlift_mobile/core/network_provider.dart';
import 'package:mindlift_mobile/core/offline_store.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  test('changes made offline are kept on disk, replayed in order when online, and a rejected one is set aside', () async {
    final dir = await Directory.systemTemp.createTemp('outbox_test');
    final file = File('${dir.path}/store.json');
    final hits = <String>[];

    // Start with nothing listening: the phone is "offline".
    final probe = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final port = probe.port;
    await probe.close(force: true);

    final store = OfflineStore(fileProvider: () async => file);
    final container = ProviderContainer(overrides: [
      serverUrlProvider.overrideWith((ref) => ServerUrlNotifier()..setUrl('http://127.0.0.1:$port')),
      tokenStorageProvider.overrideWithValue(InMemoryTokenStorage()),
      offlineStoreProvider.overrideWithValue(store),
    ]);
    addTearDown(container.dispose);

    final outbox = container.read(outboxProvider.notifier);
    await outbox.enqueue('POST', '/ok-1', {'n': 1}, 'First');
    await outbox.enqueue('POST', '/reject', {'n': 2}, 'Second');
    await outbox.enqueue('POST', '/ok-3', {'n': 3}, 'Third');
    expect(container.read(outboxProvider).pending, 3);
    expect(container.read(outboxProvider).online, isFalse);

    // Still offline: nothing is lost and nothing is marked failed.
    await outbox.flush();
    expect(container.read(outboxProvider).pending, 3);
    expect(container.read(outboxProvider).failed, isEmpty);

    // Bring the server up on the same port.
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, port);
    addTearDown(() => server.close(force: true));
    server.listen((req) async {
      hits.add(req.uri.path);
      await utf8.decoder.bind(req).join();
      if (req.uri.path == '/reject') {
        req.response.statusCode = 422;
        req.response.headers.contentType = ContentType.json;
        req.response.write(jsonEncode({'detail': 'Subject is required.'}));
      } else {
        req.response.statusCode = 200;
        req.response.headers.contentType = ContentType.json;
        req.response.write('{}');
      }
      await req.response.close();
    });

    await outbox.flush();
    expect(hits, ['/ok-1', '/reject', '/ok-3']);
    final s = container.read(outboxProvider);
    expect(s.pending, 0);
    expect(s.online, isTrue);
    expect(s.failed.single.label, 'Second');
    expect(s.failed.single.error, 'Subject is required.');

    // The set-aside change survives an app restart.
    await Future<void>.delayed(const Duration(milliseconds: 600));
    final reopened = OfflineStore(fileProvider: () async => file);
    final saved = await reopened.outbox();
    expect(saved.single.label, 'Second');
    expect(saved.single.error, 'Subject is required.');

    // Cached pages come back too.
    await store.putCache('leads:mine', {'total': 1});
    await Future<void>.delayed(const Duration(milliseconds: 600));
    final again = await OfflineStore(fileProvider: () async => file).getCache('leads:mine');
    expect((again!.data as Map)['total'], 1);
  });
}
