import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import 'lead_actions.dart';
import '../../../core/top_bar_actions.dart';
import 'ocr_lead_scan_sheet.dart';

/// The leads workspace. It mirrors the web list: the same views (My open leads, All open, New, Not interested,
/// All leads and any saved view), search, list and board layouts, row actions, bulk actions and status changes.
class LeadsScreen extends ConsumerStatefulWidget {
  const LeadsScreen({super.key});

  @override
  ConsumerState<LeadsScreen> createState() => _LeadsScreenState();
}

class _LeadsScreenState extends ConsumerState<LeadsScreen> {
  static const _pageSize = 30;
  static const _boardPageSize = 100;

  final _search = TextEditingController();
  final _scroll = ScrollController();
  Timer? _debounce;

  LeadWorkspaceConfig _cfg = const LeadWorkspaceConfig();
  final List<LeadView> _builtIn = LeadView.builtIn();
  List<LeadView> _saved = [];
  String _viewKey = 'all';

  List<LeadRow> _items = [];
  int _page = 1;
  int _total = 0;
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;
  bool _board = false;
  final Set<String> _selected = {};

  List<LeadView> get _views => [..._builtIn, ..._saved];
  LeadView get _view => _views.firstWhere((v) => v.key == _viewKey, orElse: () => _builtIn[1]);
  bool get _selecting => _selected.isNotEmpty;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
    _boot();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _boot() async {
    final svc = ref.read(leadsServiceProvider);
    // Config and saved views load beside the first page of leads.
    unawaited(svc.loadWorkspaceConfig().then((c) {
      if (mounted) setState(() => _cfg = c);
    }).catchError((_) {}));
    unawaited(svc.loadSavedViews().then((v) {
      if (mounted) setState(() => _saved = v);
    }));
    await _load();
  }

  void _onScroll() {
    if (_board || _loadingMore || _loading) return;
    if (_scroll.position.pixels > _scroll.position.maxScrollExtent - 400 && _items.length < _total) {
      _load(more: true);
    }
  }

  Future<void> _load({bool more = false}) async {
    if (more) {
      setState(() => _loadingMore = true);
    } else {
      setState(() {
        _loading = _items.isEmpty;
        _error = null;
      });
    }
    try {
      final page = more ? _page + 1 : 1;
      final v = _view;
      final result = await ref.read(leadsServiceProvider).queryPage(
            search: _search.text,
            filter: v.filter,
            sortField: v.sortField ?? 'created_at',
            sortDir: v.sortDir ?? 'desc',
            page: page,
            pageSize: _board ? _boardPageSize : _pageSize,
          );
      if (!mounted) return;
      setState(() {
        _items = more ? [..._items, ...result.items] : result.items;
        _page = result.page;
        _total = result.total;
        _selected.removeWhere((id) => !_items.any((l) => l.id == id));
      });
    } catch (e) {
      if (mounted && !more) setState(() => _error = 'We could not load your leads. Pull to retry.');
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _loadingMore = false;
        });
      }
    }
  }

  void _selectView(String key) {
    if (key == _viewKey) return;
    setState(() {
      _viewKey = key;
      _items = [];
      _selected.clear();
    });
    _load();
  }

  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (mounted) _load();
    });
  }

  Future<void> _refreshAll() async {
    final v = await ref.read(leadsServiceProvider).loadSavedViews();
    if (mounted) setState(() => _saved = v);
    await _load();
  }

  LeadActions get _actions => LeadActions(
        context: context,
        ref: ref,
        cfg: _cfg,
        views: _views,
        currentViewKey: _viewKey,
        onChanged: () async {
          await _refreshAll();
        },
        onStatusChanged: (id, status) {
          if (!mounted) return;
          setState(() => _items = _items.map((l) => l.id == id ? l.copyWith(status: status) : l).toList());
          // The row may no longer belong to this view.
          _load();
        },
      );

  void _toggleSelect(LeadRow lead) {
    setState(() {
      if (!_selected.add(lead.id)) _selected.remove(lead.id);
    });
  }

  void _open(LeadRow lead) {
    if (_selecting) {
      _toggleSelect(lead);
    } else {
      context.push('/leads/${lead.id}');
    }
  }

  // ---------------------------------------------------------------- build

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: _selecting ? '${_selected.length} selected' : 'Leads',
      subtitle: _selecting ? null : '$_total ${_view.label.toLowerCase()}',
      leading: _selecting
          ? GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: () => setState(_selected.clear),
              child: Padding(
                padding: const EdgeInsets.only(right: MlSpacing.sm),
                child: MlIcon(MlIcons.close, size: 20, color: MlColors.textPrimary),
              ),
            )
          : null,
      actions: _selecting
          ? [
              if (_cfg.canEdit)
                MlIconButton(label: 'Change status', icon: MlIcon(MlIcons.check, size: 20), onPressed: _bulkStatus),
              if (_cfg.canEdit)
                MlIconButton(label: 'Change view', icon: MlIcon(MlIcons.folder, size: 18), onPressed: _bulkMove),
              if (_cfg.canDelete)
                MlIconButton(label: 'Delete', icon: MlIcon(MlIcons.delete, size: 18), onPressed: _bulkDelete),
            ]
          : [
              MlIconButton(
                label: _board ? 'List view' : 'Board view',
                icon: MlIcon(_board ? MlIcons.list : MlIcons.board, size: 24),
                onPressed: () {
                  setState(() {
                    _board = !_board;
                    _items = [];
                  });
                  _load();
                },
              ),
              MlIconButton(
                label: 'Scan card',
                icon: MlIcon(MlIcons.camera, size: 18),
                onPressed: () => OcrLeadScanSheet.show(context, onLeadCreated: _refreshAll),
              ),
              if (_cfg.canCreate)
                MlIconButton(
                  label: 'New lead',
                  icon: MlIcon(MlIcons.add, size: 22, color: MlColors.primary),
                  onPressed: () => _actions.editLead(null),
                ),
              ...topBarActions(),
            ],
      body: Column(
        children: [
          _viewTabs(),
          Padding(
            padding: const EdgeInsets.fromLTRB(MlSpacing.md, 0, MlSpacing.md, MlSpacing.sm),
            child: MlSearchField(
              placeholder: 'Search name, email, company or phone',
              controller: _search,
              onChanged: _onSearchChanged,
              onClear: () {
                _search.clear();
                _load();
              },
            ),
          ),
          Expanded(child: _board ? _boardBody() : _listBody()),
        ],
      ),
    );
  }

  Widget _viewTabs() {
    return SizedBox(
      height: 46,
      child: ListView.builder(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 6),
        itemCount: _views.length,
        itemBuilder: (context, i) {
          final v = _views[i];
          final on = v.key == _viewKey;
          return GestureDetector(
            onTap: () => _selectView(v.key),
            child: Container(
              margin: const EdgeInsets.only(right: 8),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
              decoration: BoxDecoration(
                color: on ? MlColors.primary : MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.full),
                border: Border.all(color: on ? MlColors.primary : MlColors.border),
              ),
              child: Text(
                v.label,
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: on ? FontWeight.w700 : FontWeight.w500,
                  color: on ? const Color(0xFFFFFFFF) : MlColors.textSecondary,
                  decoration: TextDecoration.none,
                ),
              ),
            ),
          );
        },
      ),
    );
  }

  Widget _empty() {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        const SizedBox(height: 60),
        MlEmptyState(
          title: _error ?? 'No leads here',
          subtitle: _error != null ? 'Check your connection and try again.' : 'Leads from web forms, WhatsApp, email, imports and card scans appear here.',
          actionLabel: _cfg.canCreate && _error == null ? 'New lead' : 'Retry',
          onAction: () => _error == null && _cfg.canCreate ? _actions.editLead(null) : _load(),
        ),
      ],
    );
  }

  Widget _listBody() {
    return MlPullToRefresh(
      onRefresh: _refreshAll,
      child: _loading && _items.isEmpty
          ? ListView.builder(
              padding: const EdgeInsets.all(MlSpacing.md),
              itemCount: 5,
              itemBuilder: (_, __) => const Padding(padding: EdgeInsets.only(bottom: MlSpacing.sm), child: MlSkeleton(height: 84)),
            )
          : _items.isEmpty
              ? _empty()
              : ListView.builder(
                  controller: _scroll,
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(MlSpacing.md, 0, MlSpacing.md, MlSpacing.lg),
                  itemCount: _items.length + (_loadingMore ? 1 : 0),
                  itemBuilder: (context, i) {
                    if (i >= _items.length) {
                      return const Padding(padding: EdgeInsets.all(MlSpacing.md), child: Center(child: Text('Loading more…')));
                    }
                    final lead = _items[i];
                    return RepaintBoundary(
                      child: _LeadCard(
                        lead: lead,
                        cfg: _cfg,
                        selected: _selected.contains(lead.id),
                        selecting: _selecting,
                        onTap: () => _open(lead),
                        onLongPress: () => _toggleSelect(lead),
                        onMenu: () => _actions.rowMenu(lead),
                      ),
                    );
                  },
                ),
    );
  }

  Widget _boardBody() {
    final statuses = _cfg.settableStatuses;
    return MlPullToRefresh(
      onRefresh: _refreshAll,
      child: _loading && _items.isEmpty
          ? ListView(children: const [Padding(padding: EdgeInsets.all(MlSpacing.md), child: MlSkeleton(height: 200))])
          : ListView.builder(
              scrollDirection: Axis.horizontal,
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.symmetric(horizontal: MlSpacing.sm),
              itemCount: statuses.length,
              itemBuilder: (context, i) {
                final s = statuses[i];
                final col = _items.where((l) => l.status == s.value).toList();
                return Container(
                  width: 280,
                  margin: const EdgeInsets.only(right: MlSpacing.sm, bottom: MlSpacing.sm),
                  decoration: BoxDecoration(
                    color: MlColors.surfaceElevated,
                    borderRadius: BorderRadius.circular(MlRadii.lg),
                  ),
                  child: Column(
                    children: [
                      Padding(
                        padding: const EdgeInsets.all(MlSpacing.sm),
                        child: Row(
                          children: [
                            Container(width: 10, height: 10, decoration: BoxDecoration(color: statusColor(s.value), shape: BoxShape.circle)),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(s.label,
                                  style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: MlColors.textPrimary, decoration: TextDecoration.none)),
                            ),
                            Text('${col.length}', style: TextStyle(fontSize: 13, color: MlColors.textMuted, decoration: TextDecoration.none)),
                          ],
                        ),
                      ),
                      Expanded(
                        child: col.isEmpty
                            ? Center(child: Text('No leads', style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)))
                            : ListView.builder(
                                padding: const EdgeInsets.fromLTRB(MlSpacing.xs, 0, MlSpacing.xs, MlSpacing.sm),
                                itemCount: col.length,
                                itemBuilder: (context, j) => _LeadCard(
                                  lead: col[j],
                                  cfg: _cfg,
                                  compact: true,
                                  selected: _selected.contains(col[j].id),
                                  selecting: _selecting,
                                  onTap: () => _open(col[j]),
                                  onLongPress: () => _toggleSelect(col[j]),
                                  onMenu: () => _actions.rowMenu(col[j]),
                                ),
                              ),
                      ),
                    ],
                  ),
                );
              },
            ),
    );
  }

  // ---------------------------------------------------------------- bulk

  Future<void> _bulkStatus() => _actions.bulkStatus(_selected.toList(), () => setState(_selected.clear));
  Future<void> _bulkMove() => _actions.bulkMove(_selected.toList(), () => setState(_selected.clear));
  Future<void> _bulkDelete() => _actions.bulkDelete(_selected.toList(), () => setState(_selected.clear));
}

Color statusColor(String status) {
  switch (status) {
    case 'new':
      return MlColors.info;
    case 'working':
      return MlColors.warning;
    case 'qualified':
    case 'converted':
      return MlColors.success;
    case 'disqualified':
      return MlColors.danger;
    default:
      return MlColors.primary;
  }
}

MlStatusState statusState(String status) {
  switch (status) {
    case 'qualified':
    case 'converted':
      return MlStatusState.success;
    case 'disqualified':
      return MlStatusState.danger;
    case 'working':
      return MlStatusState.warning;
    case 'new':
      return MlStatusState.primary;
    default:
      return MlStatusState.neutral;
  }
}

String shortDate(DateTime utc) {
  final t = utc.toLocal();
  const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return '${t.day} ${m[t.month - 1]} ${t.year}';
}

class _LeadCard extends StatelessWidget {
  const _LeadCard({
    required this.lead,
    required this.cfg,
    required this.onTap,
    required this.onLongPress,
    required this.onMenu,
    this.selected = false,
    this.selecting = false,
    this.compact = false,
  });

  final LeadRow lead;
  final LeadWorkspaceConfig cfg;
  final bool selected;
  final bool selecting;
  final bool compact;
  final VoidCallback onTap;
  final VoidCallback onLongPress;
  final VoidCallback onMenu;

  @override
  Widget build(BuildContext context) {
    final hasNote = lead.description != null && lead.description!.trim().isNotEmpty;
    final who = lead.createdByName ?? lead.ownerName;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      onLongPress: onLongPress,
      child: Container(
        margin: const EdgeInsets.only(bottom: MlSpacing.sm),
        padding: const EdgeInsets.all(MlSpacing.md),
        decoration: BoxDecoration(
          color: selected ? MlColors.primarySoft : MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.lg),
          border: Border.all(color: selected ? MlColors.primary : MlColors.border, width: selected ? 1.5 : 1),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                if (selecting)
                  Padding(
                    padding: const EdgeInsets.only(right: 10),
                    child: AnimatedSwitcher(
                      duration: const Duration(milliseconds: 160),
                      child: MlIcon(selected ? MlIcons.selectOn : MlIcons.selectOff,
                          key: ValueKey(selected), size: 24, color: selected ? MlColors.primary : MlColors.textMuted),
                    ),
                  )
                else
                  Padding(
                    padding: const EdgeInsets.only(right: 12),
                    child: MlAvatar(name: lead.title, size: compact ? MlAvatarSize.sm : MlAvatarSize.md),
                  ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Flexible(
                            child: Text(lead.title,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: MlColors.textPrimary, decoration: TextDecoration.none)),
                          ),
                          if (hasNote) const Padding(padding: EdgeInsets.only(left: 6), child: MlIcon(MlIcons.note, size: 18)),
                        ],
                      ),
                      const SizedBox(height: 2),
                      Text(lead.subtitle,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(fontSize: 12.5, color: MlColors.textMuted, decoration: TextDecoration.none)),
                    ],
                  ),
                ),
                if (!selecting)
                  GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: onMenu,
                    child: Padding(
                      padding: const EdgeInsets.only(left: 8, top: 2, bottom: 2),
                      child: Text('⋮', style: TextStyle(fontSize: 22, color: MlColors.textMuted, fontWeight: FontWeight.w700, decoration: TextDecoration.none)),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 6,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                MlStatusChip(label: cfg.statusLabel(lead.status), state: statusState(lead.status)),
                if (!compact && lead.source != null)
                  Text(cfg.sourceLabel(lead.source), style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)),
                if (!compact && who != null)
                  Text('· $who', style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)),
                Text('· ${shortDate(lead.createdAtUtc)}', style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
