import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../../core/network_provider.dart';
import '../../../core/offline_store.dart';
import '../../workspace/workspace_widgets.dart' show DocumentsSection;
import 'lead_actions.dart';
import 'lead_tools.dart';
import 'leads_screen.dart' show statusColor, statusState, shortDate;

/// One lead, with the same actions as the web detail page: status, edit, notes, move to engagement and convert.
class LeadDetailScreen extends ConsumerStatefulWidget {
  const LeadDetailScreen({super.key, required this.leadId});
  final String leadId;

  @override
  ConsumerState<LeadDetailScreen> createState() => _LeadDetailScreenState();
}

class _LeadDetailScreenState extends ConsumerState<LeadDetailScreen> {
  bool _isLoading = true;
  Map<String, dynamic>? _raw;
  List<LeadNoteDto> _notes = [];
  LeadWorkspaceConfig _cfg = const LeadWorkspaceConfig();
  final _noteController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
    ref.read(leadsServiceProvider).loadWorkspaceConfig().then((c) {
      if (mounted) setState(() => _cfg = c);
    }).catchError((_) {});
  }

  @override
  void dispose() {
    _noteController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final svc = ref.read(leadsServiceProvider);
      final raw = await svc.getLeadRaw(widget.leadId);
      final notes = await svc.getLeadNotes(widget.leadId);
      if (mounted) {
        setState(() {
          _raw = raw;
          _notes = notes;
        });
      }
    } catch (_) {
      if (mounted && _raw == null) MlToast.show(context, message: 'We could not open this lead.', type: MlToastType.error);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  LeadRow? get _row {
    final r = _raw;
    if (r == null) return null;
    final full = '${r['firstName'] ?? ''} ${r['lastName'] ?? ''}'.trim();
    return LeadRow.fromJson({...r, 'fullName': full});
  }

  LeadActions get _actions => LeadActions(
        context: context,
        ref: ref,
        cfg: _cfg,
        views: LeadView.builtIn(),
        currentViewKey: '',
        onChanged: _load,
        onStatusChanged: (_, __) => _load(),
      );

  void _addNote() {
    _noteController.clear();
    MlSheet.show<void>(
      context: context,
      title: 'Add note',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            MlTextField(label: 'Note', placeholder: 'Conversation or progress notes…', controller: _noteController, maxLines: 5, minLines: 3),
            const SizedBox(height: MlSpacing.md),
            MlButton(
              label: 'Save note',
              onPressed: () async {
                final text = _noteController.text.trim();
                if (text.isEmpty) return;
                Navigator.of(sheetContext).pop();
                try {
                  LeadNoteDto? added;
                  final sent = await sendOrQueue(
                    ref,
                    send: () async => added = await ref.read(leadsServiceProvider).addLeadNote(widget.leadId, text),
                    method: 'POST',
                    path: '/api/v1/leads/${widget.leadId}/notes',
                    body: {'content': text},
                    label: 'Add note to lead',
                  );
                  if (mounted && added != null) setState(() => _notes = [added!, ..._notes]);
                  if (mounted) MlToast.show(context, message: sent ? 'Note added.' : 'Saved on this phone. It will sync when you are back online.');
                } catch (e) {
                  if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'We could not add the note.'), type: MlToastType.error);
                }
              },
            ),
          ],
        ),
      ),
    );
  }

  void _editNote(LeadNoteDto n) {
    _noteController.text = n.text;
    MlSheet.show<void>(
      context: context,
      title: 'Edit note',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            MlTextField(label: 'Note', controller: _noteController, maxLines: 5, minLines: 3),
            const SizedBox(height: MlSpacing.md),
            MlButton(
              label: 'Save changes',
              onPressed: () async {
                final text = _noteController.text.trim();
                if (text.isEmpty) return;
                Navigator.of(sheetContext).pop();
                try {
                  final updated = await ref.read(leadsServiceProvider).updateLeadNote(widget.leadId, n.id, text);
                  if (mounted) setState(() => _notes = [for (final x in _notes) x.id == n.id ? updated : x]);
                  if (mounted) MlToast.show(context, message: 'Note updated.');
                } catch (e) {
                  if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'We could not update the note.'), type: MlToastType.error);
                }
              },
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _convertToContact(LeadRow lead) async {
    try {
      await ref.read(leadsServiceProvider).quickConvertToContact(lead.id);
      if (mounted) MlToast.show(context, message: 'Converted to a contact.');
      await _load();
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'We could not convert this lead.'), type: MlToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final back = MlIconButton(
      label: 'Back',
      icon: MlIcon(MlIcons.back, size: 20, color: MlColors.textPrimary),
      onPressed: () => context.pop(),
    );

    final lead = _row;
    if (_isLoading && lead == null) {
      return MlScreen(
        title: 'Loading lead…',
        leading: back,
        body: const Padding(
          padding: EdgeInsets.all(MlSpacing.lg),
          child: Column(children: [MlSkeleton(height: 120), SizedBox(height: MlSpacing.md), MlSkeleton(height: 200)]),
        ),
      );
    }
    if (lead == null) {
      return MlScreen(title: 'Lead', leading: back, body: Center(child: Text('This lead could not be opened.'.tr)));
    }

    final raw = _raw!;
    final tags = ((raw['tags'] as List?) ?? const []).map((e) => e.toString()).toList();
    final canEdit = _cfg.canEdit && !lead.isConverted;

    return MlScreen(
      title: lead.title,
      subtitle: lead.fullName.isNotEmpty && lead.companyName != null ? 'Contact: ${lead.fullName}' : 'Lead',
      leading: back,
      actions: [
        if (canEdit) MlIconButton(label: 'Edit', icon: MlIcon(MlIcons.edit, size: 20), onPressed: () => _actions.editLead(lead)),
      ],
      bottomRail: Container(
        padding: const EdgeInsets.all(MlSpacing.md),
        decoration: BoxDecoration(color: MlColors.surface, border: Border(top: BorderSide(color: MlColors.border))),
        child: Row(
          children: [
            Expanded(child: MlButton(label: '+ Add note', onPressed: _addNote, variant: MlButtonVariant.primary)),
            if (canEdit) ...[
              const SizedBox(width: MlSpacing.sm),
              Expanded(child: MlButton(label: 'Change status', onPressed: () => _actions.changeStatus(lead), variant: MlButtonVariant.secondary)),
            ],
          ],
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Status stepper, like the web header.
            if (!lead.isConverted)
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final s in _cfg.settableStatuses)
                      GestureDetector(
                        onTap: canEdit && s.value != lead.status ? () => _actions.changeStatus(lead) : null,
                        child: Container(
                          margin: const EdgeInsetsDirectional.only(end: 8),
                          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
                          decoration: BoxDecoration(
                            color: s.value == lead.status ? statusColor(s.value) : MlColors.surface,
                            borderRadius: BorderRadius.circular(MlRadii.full),
                            border: Border.all(color: s.value == lead.status ? statusColor(s.value) : MlColors.border),
                          ),
                          child: Text(s.label,
                              style: TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w700,
                                  color: s.value == lead.status ? const Color(0xFFFFFFFF) : MlColors.textSecondary,
                                  decoration: TextDecoration.none)),
                        ),
                      ),
                  ],
                ),
              )
            else
              Align(alignment: AlignmentDirectional.centerStart, child: MlStatusChip(label: 'Converted', state: MlStatusState.success)),
            const SizedBox(height: MlSpacing.md),

            // Reach the person in one tap
            if ((lead.phone ?? '').trim().isNotEmpty || (lead.email ?? '').trim().isNotEmpty) ...[
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  if ((lead.phone ?? '').trim().isNotEmpty) ...[
                    _chipButton(MlIcons.call, 'Call', () => _actions.callLead(lead)),
                    _chipButton(MlIcons.whatsapp, 'WhatsApp', () => _open(Uri.parse('https://wa.me/${lead.phone!.replaceAll(RegExp(r'[^0-9]'), '')}'))),
                    _chipButton(MlIcons.sms, 'SMS', () => _open(Uri(scheme: 'sms', path: lead.phone!.trim()))),
                  ],
                  if ((lead.email ?? '').trim().isNotEmpty) _chipButton(MlIcons.email, 'Email', () => _open(Uri(scheme: 'mailto', path: lead.email!.trim()))),
                ],
              ),
              const SizedBox(height: MlSpacing.md),
            ],

            // Actions
            if (canEdit)
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  _chipButton(MlIcons.handshake, 'Move to engagement', () => _actions.moveToEngagement(lead)),
                  _chipButton(MlIcons.person, 'Convert to contact', () => _convertToContact(lead)),
                  _chipButton(MlIcons.swap, 'Convert…', () => LeadTools(context: context, ref: ref, cfg: _cfg).convert(lead, _load)),
                  _chipButton(MlIcons.copy, 'Find duplicates', () => LeadTools(context: context, ref: ref, cfg: _cfg).duplicates(lead, () async {
                        await _load();
                      })),
                  _chipButton(MlIcons.note, 'Quick note', () => _actions.stickyNote(lead)),
                  if (_cfg.canDelete)
                    _chipButton(MlIcons.delete, 'Delete', () async {
                      await _actions.deleteLead(lead);
                      if (mounted && context.canPop()) context.pop();
                    }, danger: true),
                ],
              ),
            if (canEdit) const SizedBox(height: MlSpacing.md),

            // Details
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      MlAvatar(name: lead.title, size: MlAvatarSize.lg),
                      const SizedBox(width: MlSpacing.md),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(lead.fullName.isEmpty ? lead.title : lead.fullName,
                                style: MlTypography.headingSmall.copyWith(color: MlColors.textPrimary)),
                            const SizedBox(height: 4),
                            MlStatusChip(label: _cfg.statusLabel(lead.status), state: statusState(lead.status)),
                          ],
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.md),
                  _detailRow('Company', lead.companyName),
                  _detailRow('Email', lead.email),
                  _detailRow('Phone', lead.phone),
                  _detailRow('Source', lead.source == null ? null : _cfg.sourceLabel(lead.source)),
                  _detailRow('Rating', lead.rating),
                  _detailRow('Address', [lead.address, lead.city, lead.state].where((e) => e != null && e.isNotEmpty).join(', ')),
                  _detailRow('Created', shortDate(lead.createdAtUtc)),
                  if (raw['firstContactedAtUtc'] != null)
                    _detailRow('First contacted', shortDate(DateTime.parse(raw['firstContactedAtUtc'] as String))),
                  if ((raw['disqualifyReason'] as String?)?.isNotEmpty == true) _detailRow('Not interested because', raw['disqualifyReason'] as String),
                  if (tags.isNotEmpty) _detailRow('Tags', tags.join(', ')),
                ],
              ),
            ),
            if (lead.description != null && lead.description!.trim().isNotEmpty) ...[
              const SizedBox(height: MlSpacing.md),
              Container(
                padding: const EdgeInsets.all(MlSpacing.md),
                decoration: BoxDecoration(
                  color: MlColors.warningSoft,
                  borderRadius: BorderRadius.circular(MlRadii.lg),
                ),
                child: Text(lead.description!.trim(),
                    style: TextStyle(fontSize: 14, color: MlColors.textPrimary, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
              ),
            ],
            const SizedBox(height: MlSpacing.lg),

            DocumentsSection(entityType: 'leads', entityId: widget.leadId, canAdd: _cfg.canEdit),
            const SizedBox(height: MlSpacing.md),

            // Notes
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('Notes (${_notes.length})', style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)),
                GestureDetector(
                  onTap: _addNote,
                  child: Text('+ Add'.tr, style: MlTypography.labelMedium.copyWith(color: MlColors.primaryLight)),
                ),
              ],
            ),
            const SizedBox(height: MlSpacing.sm),
            if (_notes.isEmpty)
              Container(
                padding: const EdgeInsets.all(MlSpacing.xl),
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: MlColors.surface,
                  borderRadius: BorderRadius.circular(MlRadii.md),
                  border: Border.all(color: MlColors.border),
                ),
                child: Text('No notes recorded yet'.tr, style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted)),
              )
            else
              for (final n in _notes)
                Container(
                  margin: const EdgeInsets.only(bottom: MlSpacing.sm),
                  padding: const EdgeInsets.all(MlSpacing.md),
                  decoration: BoxDecoration(
                    color: MlColors.surface,
                    borderRadius: BorderRadius.circular(MlRadii.md),
                    border: Border.all(color: MlColors.border),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Expanded(
                            child: Text('${n.authorName} · ${shortDate(n.createdAtUtc)}',
                                style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                          ),
                          if (_cfg.canEdit && n.id.isNotEmpty && n.id != '00000000-0000-0000-0000-000000000000')
                            GestureDetector(
                              onTap: () => _editNote(n),
                              child: Text('Edit'.tr, style: TextStyle(fontSize: 12, color: MlColors.primaryLight, decoration: TextDecoration.none)),
                            ),
                        ],
                      ),
                      const SizedBox(height: 4),
                      Text(n.text, style: TextStyle(fontSize: 14.5, color: MlColors.textPrimary, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                    ],
                  ),
                ),
          ],
        ),
      ),
    );
  }

  Future<void> _open(Uri uri) async {
    try {
      if (!await launchUrl(uri, mode: LaunchMode.externalApplication) && mounted) {
        MlToast.show(context, message: 'No app found to open this.');
      }
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'No app found to open this.');
    }
  }

  Widget _chipButton(IconData icon, String label, VoidCallback onTap, {bool danger = false}) {
    final color = danger ? MlColors.danger : MlColors.textPrimary;
    return MlPressable(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.fromLTRB(12, 9, 16, 9),
        decoration: BoxDecoration(
          color: MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.full),
          border: Border.all(color: danger ? MlColors.danger : MlColors.border),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            MlIcon(icon, size: 19, color: danger ? MlColors.danger : MlColors.primary),
            const SizedBox(width: 7),
            Text(MlStrings.t(label), style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600, color: color, decoration: TextDecoration.none)),
          ],
        ),
      ),
    );
  }

  Widget _detailRow(String label, String? value) {
    if (value == null || value.trim().isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 120, child: Text(label, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))),
          Expanded(child: Text(value, style: MlTypography.bodySmall.copyWith(color: MlColors.textPrimary))),
        ],
      ),
    );
  }
}
