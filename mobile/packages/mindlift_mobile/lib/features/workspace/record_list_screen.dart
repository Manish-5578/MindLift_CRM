import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../core/network_provider.dart';
import '../../core/offline_store.dart';
import '../../core/top_bar_actions.dart';

typedef RowBuilder = Widget Function(BuildContext context, Map<String, dynamic> item);

/// A searchable, endlessly scrolling list of contacts, accounts or deals. The last page seen is kept for offline use.
class RecordListScreen extends ConsumerStatefulWidget {
  const RecordListScreen({
    super.key,
    required this.title,
    required this.entity,
    required this.rowBuilder,
    required this.searchHint,
    required this.emptyTitle,
    required this.emptyIcon,
    this.onAdd,
    this.headerBuilder,
  });

  final String title;

  /// contacts, accounts or deals
  final String entity;
  final RowBuilder rowBuilder;
  final String searchHint;
  final String emptyTitle;
  final IconData emptyIcon;
  final Future<void> Function(BuildContext context, WidgetRef ref)? onAdd;
  final Widget Function(int total)? headerBuilder;

  @override
  ConsumerState<RecordListScreen> createState() => RecordListScreenState();
}

class RecordListScreenState extends ConsumerState<RecordListScreen> {
  final _search = TextEditingController();
  final _scroll = ScrollController();
  List<Map<String, dynamic>> _items = [];
  int _total = 0;
  int _page = 1;
  bool _loading = true;
  bool _more = false;
  String? _error;
  bool _offline = false;
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_more || _loading || _items.length >= _total) return;
      if (_scroll.position.pixels > _scroll.position.maxScrollExtent - 400) _load(more: true);
    });
    _load();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _load({bool more = false}) async {
    if (more) {
      setState(() => _more = true);
    } else {
      setState(() {
        _loading = _items.isEmpty;
        _error = null;
      });
    }
    try {
      final page = more ? _page + 1 : 1;
      final r = await ref.read(workspaceServiceProvider).query(widget.entity, search: _search.text, page: page);
      if (!mounted) return;
      setState(() {
        _items = more ? [..._items, ...r.items] : r.items;
        _total = r.total;
        _page = r.page;
        _offline = false;
      });
      if (!more && _search.text.trim().isEmpty) unawaited(ref.read(offlineStoreProvider).putCache('list:${widget.entity}', r.toJson()));
    } catch (e) {
      if (isOfflineError(e) && !more) {
        ref.read(outboxProvider.notifier).markOffline();
        final saved = _search.text.trim().isEmpty ? await ref.read(offlineStoreProvider).getCache('list:${widget.entity}') : null;
        if (mounted) {
          if (saved != null) {
            final p = RecordPage.fromJson(Map<String, dynamic>.from(saved.data as Map));
            setState(() {
              _items = p.items;
              _total = p.total;
              _page = 1;
              _offline = true;
            });
          } else {
            setState(() => _error = 'You are offline and this list has not been opened before.');
          }
        }
      } else if (mounted && !more) {
        setState(() => _error = 'We could not load this list. Pull down to retry.');
      }
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _more = false;
        });
      }
    }
  }

  Future<void> refresh() => _load();

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: widget.title,
      subtitle: _loading ? null : '$_total ${_total == 1 ? 'record' : 'records'}${_offline ? ' · saved copy' : ''}',
      leading: MlIconButton(label: 'Back', icon: const MlIcon(MlIcons.back, size: 24), onPressed: () => context.canPop() ? context.pop() : context.go('/home')),
      actions: [
        if (widget.onAdd != null)
          MlIconButton(
            label: 'Add',
            icon: const MlIcon(MlIcons.add, size: 26, color: MlColors.primary),
            onPressed: () async {
              await widget.onAdd!(context, ref);
              if (mounted) _load();
            },
          ),
        ...topBarActions(),
      ],
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(MlSpacing.md, 4, MlSpacing.md, MlSpacing.sm),
            child: MlTextField(
              controller: _search,
              placeholder: widget.searchHint,
              prefix: MlIcon(MlIcons.search, size: 20, color: MlColors.textMuted),
              onChanged: (_) {
                _debounce?.cancel();
                _debounce = Timer(const Duration(milliseconds: 350), _load);
              },
            ),
          ),
          if (widget.headerBuilder != null && !_loading) widget.headerBuilder!(_total),
          Expanded(
            child: MlPullToRefresh(
              onRefresh: _load,
              child: _loading
                  ? ListView.builder(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      itemCount: 6,
                      itemBuilder: (_, __) => const Padding(padding: EdgeInsets.only(bottom: MlSpacing.sm), child: MlSkeleton(height: 66, borderRadius: MlRadii.lg)),
                    )
                  : _items.isEmpty
                      ? ListView(
                          physics: const AlwaysScrollableScrollPhysics(),
                          children: [
                            const SizedBox(height: 60),
                            MlEmptyState(
                              title: _error ?? widget.emptyTitle,
                              icon: MlIcon(_error != null ? MlIcons.warning : widget.emptyIcon, size: 30, color: MlColors.primary),
                              subtitle: _error != null ? null : (_search.text.isEmpty ? null : 'Try a different search.'),
                            ),
                          ],
                        )
                      : ListView.builder(
                          controller: _scroll,
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.fromLTRB(MlSpacing.md, 0, MlSpacing.md, MlSpacing.lg),
                          itemCount: _items.length + (_more ? 1 : 0),
                          itemBuilder: (context, i) {
                            if (i >= _items.length) return const Padding(padding: EdgeInsets.all(MlSpacing.md), child: Center(child: MlSpinner(size: 22)));
                            return widget.rowBuilder(context, _items[i]);
                          },
                        ),
            ),
          ),
        ],
      ),
    );
  }
}
