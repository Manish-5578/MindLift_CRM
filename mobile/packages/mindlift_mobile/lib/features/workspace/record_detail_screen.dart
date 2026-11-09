import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../core/network_provider.dart';
import '../../core/offline_store.dart';
import '../../core/top_bar_actions.dart';

typedef DetailBuilder = List<Widget> Function(BuildContext context, Map<String, dynamic> summary, Future<void> Function() reload);

/// A record's own page: loads its summary (saved copy when offline), shows a header and the sections the caller builds.
class RecordDetailScreen extends ConsumerStatefulWidget {
  const RecordDetailScreen({
    super.key,
    required this.entity,
    required this.id,
    required this.title,
    required this.subtitle,
    required this.builder,
    this.summaryKey,
    this.actions,
  });

  final String entity;
  final String id;

  /// The record's name, read from the loaded summary.
  final String Function(Map<String, dynamic> summary) title;
  final String? Function(Map<String, dynamic> summary) subtitle;
  final DetailBuilder builder;

  /// Where the record sits inside the summary map (contact, account); the deal endpoint has no summary, so it returns the record itself.
  final String? summaryKey;
  final List<Widget> Function(Map<String, dynamic> summary, Future<void> Function() reload)? actions;

  @override
  ConsumerState<RecordDetailScreen> createState() => _RecordDetailScreenState();
}

class _RecordDetailScreenState extends ConsumerState<RecordDetailScreen> {
  Map<String, dynamic>? _data;
  bool _loading = true;
  bool _offline = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final key = 'detail:${widget.entity}:${widget.id}';
    try {
      final svc = ref.read(workspaceServiceProvider);
      final data = widget.entity == 'deals' ? await svc.getRecord('deals', widget.id) : await svc.summary(widget.entity, widget.id);
      if (!mounted) return;
      setState(() {
        _data = data;
        _offline = false;
        _error = null;
      });
      unawaited(ref.read(offlineStoreProvider).putCache(key, data));
    } catch (e) {
      if (isOfflineError(e)) {
        ref.read(outboxProvider.notifier).markOffline();
        final saved = await ref.read(offlineStoreProvider).getCache(key);
        if (mounted) {
          if (saved != null) {
            setState(() {
              _data = Map<String, dynamic>.from(saved.data as Map);
              _offline = true;
            });
          } else {
            setState(() => _error = 'You are offline and this record has not been opened before.');
          }
        }
      } else if (mounted) {
        setState(() => _error = 'We could not open this record.');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final back = MlIconButton(label: 'Back', icon: const MlIcon(MlIcons.back, size: 24), onPressed: () => context.canPop() ? context.pop() : context.go('/home'));
    final data = _data;
    if (data == null) {
      return MlScreen(
        title: _loading ? 'Loading…' : 'Record',
        leading: back,
        body: _loading
            ? const Center(child: MlSpinner(size: 28))
            : MlEmptyState(title: _error ?? 'Not found', icon: const MlIcon(MlIcons.warning, size: 30, color: MlColors.primary), actionLabel: 'Retry', onAction: _load),
      );
    }
    return MlScreen(
      title: widget.title(data),
      subtitle: _offline ? 'Saved copy · you are offline' : widget.subtitle(data),
      leading: back,
      actions: [...(widget.actions?.call(data, _load) ?? const []), ...topBarActions()],
      body: MlPullToRefresh(
        onRefresh: _load,
        child: SingleChildScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: widget.builder(context, data, _load)),
        ),
      ),
    );
  }
}
