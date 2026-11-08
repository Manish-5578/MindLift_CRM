import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import 'lead_actions.dart';

/// Create, edit or delete a saved leads view, the same views the web app has.
class SavedViewForm extends ConsumerStatefulWidget {
  const SavedViewForm({super.key, required this.cfg, this.existing, required this.onDone});
  final LeadWorkspaceConfig cfg;
  final LeadView? existing;

  /// Called with the key of the view to show afterwards (null when deleted).
  final void Function(String? viewKey) onDone;

  static Future<void> show(BuildContext context, {required LeadWorkspaceConfig cfg, LeadView? existing, required void Function(String? viewKey) onDone}) {
    return MlSheet.show<void>(
      context: context,
      title: existing == null ? 'New view' : 'Edit view',
      builder: (_) => SavedViewForm(cfg: cfg, existing: existing, onDone: onDone),
    );
  }

  @override
  ConsumerState<SavedViewForm> createState() => _SavedViewFormState();
}

class _SavedViewFormState extends ConsumerState<SavedViewForm> {
  final _name = TextEditingController();
  final Set<String> _statuses = {};
  bool _mine = false;
  bool _shared = false;
  bool _newestFirst = true;
  bool _busy = false;
  String? _nameError;

  /// Conditions made on the web that this form has no control for; kept as they are when saving.
  List<Map<String, dynamic>> _otherConditions = [];
  List<dynamic> _groups = [];

  @override
  void initState() {
    super.initState();
    final v = widget.existing;
    if (v == null) return;
    _name.text = v.label;
    _newestFirst = (v.sortDir ?? 'desc') != 'asc';
    final f = v.filter;
    if (f != null) {
      _groups = (f['groups'] as List?) ?? [];
      for (final c in ((f['conditions'] as List?) ?? const []).whereType<Map>()) {
        final cond = Map<String, dynamic>.from(c);
        final field = cond['field'];
        final op = cond['op'];
        if (field == 'status' && (op == LeadView.opIn || op == LeadView.opEq)) {
          final val = cond['value'];
          if (val is List) _statuses.addAll(val.map((e) => '$e'));
          if (val is String) _statuses.add(val);
        } else if (field == 'owner_id' && op == LeadView.opEq && cond['value'] == '@me') {
          _mine = true;
        } else {
          _otherConditions.add(cond);
        }
      }
    }
    _shared = false;
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Map<String, dynamic> _buildFilter() => {
        'logic': 'and',
        'conditions': [
          ..._otherConditions,
          if (_mine) {'field': 'owner_id', 'op': LeadView.opEq, 'value': '@me'},
          if (_statuses.isNotEmpty) {'field': 'status', 'op': LeadView.opIn, 'value': _statuses.toList()},
        ],
        'groups': _groups,
      };

  Future<void> _save() async {
    final name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _nameError = 'Give the view a name.');
      return;
    }
    setState(() {
      _busy = true;
      _nameError = null;
    });
    try {
      final svc = ref.read(leadsServiceProvider);
      final saved = await svc.saveView(
        id: widget.existing?.savedId,
        name: name,
        filter: _buildFilter(),
        shared: _shared,
        sortField: widget.existing?.sortField ?? 'created_at',
        sortDir: _newestFirst ? 'desc' : 'asc',
      );
      if (!mounted) return;
      Navigator.of(context).pop();
      MlToast.show(context, message: widget.existing == null ? 'View created.' : 'View updated.', type: MlToastType.success);
      widget.onDone(saved.key);
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        MlToast.show(context, message: LeadActions.errorText(e, 'We could not save the view.'), type: MlToastType.error);
      }
    }
  }

  Future<void> _delete() async {
    final id = widget.existing?.savedId;
    if (id == null) return;
    setState(() => _busy = true);
    try {
      await ref.read(leadsServiceProvider).deleteSavedView(id);
      if (!mounted) return;
      Navigator.of(context).pop();
      MlToast.show(context, message: 'View deleted.', type: MlToastType.success);
      widget.onDone(null);
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        MlToast.show(context, message: LeadActions.errorText(e, 'We could not delete the view.'), type: MlToastType.error);
      }
    }
  }

  Widget _toggle(String title, String hint, bool value, ValueChanged<bool> onChanged) {
    return MlPressable(
      onTap: () => onChanged(!value),
      scale: 0.99,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                  Text(hint, style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                ],
              ),
            ),
            AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              width: 46,
              height: 28,
              padding: const EdgeInsets.all(3),
              decoration: BoxDecoration(color: value ? MlColors.primary : MlColors.surfaceMuted, borderRadius: BorderRadius.circular(14)),
              alignment: value ? Alignment.centerRight : Alignment.centerLeft,
              child: Container(width: 22, height: 22, decoration: const BoxDecoration(color: MlColors.white, shape: BoxShape.circle)),
            ),
          ],
        ),
      ),
    );
  }

  Widget _chip(String label, bool on, VoidCallback onTap) {
    return MlPressable(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 160),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          color: on ? MlColors.primary : MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.full),
          border: Border.all(color: on ? MlColors.primary : MlColors.border),
        ),
        child: Text(label,
            style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600, color: on ? MlColors.white : MlColors.textSecondary, decoration: TextDecoration.none)),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final statuses = widget.cfg.settableStatuses;
    return Padding(
      padding: const EdgeInsets.all(MlSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          MlTextField(label: 'View name', placeholder: 'e.g. Hot leads this week', controller: _name, errorText: _nameError),
          const SizedBox(height: MlSpacing.md),
          Text('Show leads with status', style: MlTypography.labelMedium.copyWith(color: MlColors.textSecondary)),
          const SizedBox(height: 4),
          Text(_statuses.isEmpty ? 'Any status' : '${_statuses.length} selected', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
          const SizedBox(height: MlSpacing.xs),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final s in statuses)
                _chip(s.label, _statuses.contains(s.value), () => setState(() => _statuses.contains(s.value) ? _statuses.remove(s.value) : _statuses.add(s.value))),
            ],
          ),
          const SizedBox(height: MlSpacing.sm),
          _toggle('Only my leads', 'Leads owned by you', _mine, (v) => setState(() => _mine = v)),
          if (widget.cfg.isAdmin) _toggle('Share with my team', 'Everyone in the company sees this view', _shared, (v) => setState(() => _shared = v)),
          _toggle('Newest first', 'Turn off to list oldest first', _newestFirst, (v) => setState(() => _newestFirst = v)),
          if (_otherConditions.isNotEmpty || _groups.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text('Other filters set on the web are kept as they are.', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
          ],
          const SizedBox(height: MlSpacing.md),
          MlButton(label: widget.existing == null ? 'Create view' : 'Save changes', isLoading: _busy, onPressed: _busy ? null : _save),
          if (widget.existing != null) ...[
            const SizedBox(height: MlSpacing.sm),
            MlButton(label: 'Delete view', variant: MlButtonVariant.ghost, onPressed: _busy ? null : _delete),
          ],
        ],
      ),
    );
  }
}
