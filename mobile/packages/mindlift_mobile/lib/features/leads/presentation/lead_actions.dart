import 'dart:async';
import 'lead_tools.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../../core/auth_provider.dart' show connectionProblem;
import '../../../core/network_provider.dart';
import '../../../core/offline_store.dart';

/// The things the web leads list can do to a lead (row menu and bulk bar), as bottom sheets.
class LeadActions {
  LeadActions({
    required this.context,
    required this.ref,
    required this.cfg,
    required this.views,
    required this.currentViewKey,
    required this.onChanged,
    this.onStatusChanged,
  });

  final BuildContext context;
  final WidgetRef ref;
  final LeadWorkspaceConfig cfg;
  final List<LeadView> views;
  final String currentViewKey;
  final Future<void> Function() onChanged;
  final void Function(String id, String status)? onStatusChanged;

  LeadsService get _svc => ref.read(leadsServiceProvider);

  // ------------------------------------------------------------ helpers

  static String errorText(Object e, String fallback) {
    if (e is FormatException && e.message.isNotEmpty) return e.message;
    if (e is DioException) {
      final d = e.response?.data;
      if (d is Map) {
        // Validation failures list the exact field problems; show the first one.
        final errs = d['errors'];
        if (errs is Map && errs.isNotEmpty) {
          final first = errs.values.first;
          if (first is List && first.isNotEmpty && first.first is String) return first.first as String;
        }
        final m = d['detail'] ?? d['title'] ?? d['message'];
        if (m is String && m.isNotEmpty) return m;
      }
      final net = connectionProblem(e);
      if (net != null) return net;
    }
    return fallback;
  }

  void _toast(String message, {bool error = false}) {
    if (!context.mounted) return;
    MlToast.show(context, message: message, type: error ? MlToastType.error : MlToastType.success);
  }

  Future<T?> _sheet<T>(String title, WidgetBuilder builder) => MlSheet.show<T>(context: context, title: title, builder: builder);

  Widget _row(IconData icon, String label, VoidCallback onTap, {bool danger = false, String? hint}) {
    return MlPressable(
      scale: 0.98,
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 13, horizontal: MlSpacing.md),
        child: Row(
          children: [
            SizedBox(width: 30, child: MlIcon(icon, size: 22, color: danger ? MlColors.danger : MlColors.primary)),
            const SizedBox(width: 8),
            Expanded(
              child: Text(MlStrings.t(label),
                  style: TextStyle(
                      fontSize: 15.5,
                      fontWeight: FontWeight.w600,
                      color: danger ? MlColors.danger : MlColors.textPrimary,
                      decoration: TextDecoration.none)),
            ),
            if (hint != null) Text(hint, style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)),
          ],
        ),
      ),
    );
  }

  // ------------------------------------------------------------ row menu

  Future<void> rowMenu(LeadRow lead) {
    return _sheet<void>(lead.title, (ctx) {
      void run(VoidCallback f) {
        Navigator.of(ctx).pop();
        f();
      }

      return Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          _row(MlIcons.chevronRight, 'Open', () => run(() => context.push('/leads/${lead.id}'))),
          if (!lead.isConverted) _row(MlIcons.handshake, 'Move to engagement', () => run(() => moveToEngagement(lead))),
          _row(MlIcons.note, 'Notes', () => run(() => stickyNote(lead))),
          if (cfg.canEdit) _row(MlIcons.edit, 'Edit', () => run(() => editLead(lead))),
          if (cfg.canEdit) _row(MlIcons.check, 'Change status', () => run(() => changeStatus(lead))),
          if (cfg.canEdit) _row(MlIcons.folder, 'Change view', () => run(() => moveToView([lead.id], lead.title))),
          if (cfg.canDelete) _row(MlIcons.delete, 'Delete', () => run(() => deleteLead(lead)), danger: true),
          const SizedBox(height: MlSpacing.sm),
        ],
      );
    });
  }

  // ------------------------------------------------------------ status

  Future<void> changeStatus(LeadRow lead) async {
    String selected = lead.status;
    final reason = TextEditingController(text: 'Not interested');
    final picked = await _sheet<String>('Change status', (ctx) {
      return StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Change status for ${lead.title}',
                  style: TextStyle(fontSize: 13, color: MlColors.textMuted, decoration: TextDecoration.none)),
              const SizedBox(height: MlSpacing.sm),
              for (final s in cfg.settableStatuses)
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTap: () => setS(() => selected = s.value),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 10),
                    child: Row(
                      children: [
                        MlIcon(selected == s.value ? MlIcons.radioOn : MlIcons.selectOff, size: 24,
                            color: selected == s.value ? MlColors.primary : MlColors.textMuted),
                        const SizedBox(width: 12),
                        Text(s.label,
                            style: TextStyle(
                                fontSize: 15.5,
                                fontWeight: selected == s.value ? FontWeight.w700 : FontWeight.w500,
                                color: MlColors.textPrimary,
                                decoration: TextDecoration.none)),
                      ],
                    ),
                  ),
                ),
              if (selected == 'disqualified') ...[
                const SizedBox(height: MlSpacing.sm),
                MlTextField(label: 'Reason', controller: reason, placeholder: 'Not interested'),
              ],
              const SizedBox(height: MlSpacing.md),
              MlButton(
                label: 'Update status',
                onPressed: selected == lead.status ? null : () => Navigator.of(ctx).pop(selected),
              ),
            ],
          ),
        );
      });
    });
    if (picked == null) return;
    try {
      final why = reason.text.trim().isEmpty ? 'Not interested' : reason.text.trim();
      final sent = await sendOrQueue(
        ref,
        send: () => picked == 'disqualified' ? _svc.disqualify(lead.id, why) : _svc.updateLeadStatus(lead.id, picked),
        method: 'POST',
        path: picked == 'disqualified' ? '/api/v1/leads/${lead.id}/disqualify' : '/api/v1/leads/${lead.id}/status',
        body: picked == 'disqualified' ? {'reason': why, 'notes': null} : {'status': picked},
        label: 'Change status of ${lead.title}',
      );
      _toast(sent ? 'Status updated.' : 'Saved on this phone. It will sync when you are back online.');
      onStatusChanged?.call(lead.id, picked);
    } catch (e) {
      _toast(errorText(e, 'We could not change the status.'), error: true);
    }
  }

  // ------------------------------------------------------------ notes

  /// Opens the dialer, and when the person comes back to the app asks how the call went and logs it on the lead.
  Future<void> callLead(LeadRow lead) async {
    final phone = (lead.phone ?? '').trim();
    if (phone.isEmpty) return;
    final back = _ResumeOnce();
    try {
      final ok = await launchUrl(Uri(scheme: 'tel', path: phone), mode: LaunchMode.externalApplication);
      if (!ok) {
        back.cancel();
        _toast('No app found to place the call.', error: true);
        return;
      }
    } catch (_) {
      back.cancel();
      _toast('No app found to place the call.', error: true);
      return;
    }
    await back.future;
    if (!context.mounted) return;

    final options = cfg.callOutcomes.isNotEmpty
        ? cfg.callOutcomes
        : const [PicklistOption(value: 'connected', label: 'Connected'), PicklistOption(value: 'no_answer', label: 'No answer')];
    final picked = await _sheet<String>('How did the call go?', (ctx) {
      return Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(MlSpacing.md, 0, MlSpacing.md, MlSpacing.sm),
            child: Align(
              alignment: AlignmentDirectional.centerStart,
              child: Text('Log it on ${lead.title}',
                  style: TextStyle(fontSize: 13, color: MlColors.textMuted, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
            ),
          ),
          for (final o in options) _row(MlIcons.call, o.label, () => Navigator.of(ctx).pop(o.value)),
          _row(MlIcons.close, 'Do not log', () => Navigator.of(ctx).pop('')),
        ],
      );
    });
    if (picked == null || picked.isEmpty) return;

    final subject = 'Call with ${lead.fullName.trim().isEmpty ? lead.title : lead.fullName.trim()}';
    final req = CreateActivityRequest(type: 'Call', subject: subject, leadId: lead.id, dueAtUtc: DateTime.now(), direction: 'Outbound', callOutcome: picked);
    try {
      // A call dated now is recorded as done, with its outcome, in one request.
      final sent = await sendOrQueue(
        ref,
        send: () => ref.read(activitiesServiceProvider).createActivity(req),
        method: 'POST',
        path: '/api/v1/activities',
        body: req.toJson(),
        label: subject,
      );
      _toast(sent ? 'Call logged.' : 'Saved on this phone. It will sync when you are back online.');
    } catch (e) {
      _toast(errorText(e, 'We could not log the call.'), error: true);
    }
  }

  Future<void> stickyNote(LeadRow lead) async {
    final c = TextEditingController(text: lead.description ?? '');
    final saved = await _sheet<bool>('Notes · ${lead.title}', (ctx) {
      return Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            MlTextField(label: 'Quick note', placeholder: 'Anything worth remembering about this lead', controller: c, maxLines: 6, minLines: 4),
            const SizedBox(height: MlSpacing.md),
            MlButton(label: 'Save note', onPressed: () => Navigator.of(ctx).pop(true)),
          ],
        ),
      );
    });
    if (saved != true) return;
    try {
      final text = c.text.trim();
      await _svc.setStickyNote(lead.id, text.isEmpty ? null : text);
      _toast('Note saved.');
      await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not save the note.'), error: true);
    }
  }

  // ------------------------------------------------------------ engagement

  Future<void> moveToEngagement(LeadRow lead) async {
    try {
      final dealId = await _svc.quickConvertToEngagement(lead.id);
      _toast('Lead moved to Engagement.');
      await onChanged();
      if (dealId != null && context.mounted) {
        // Deal screens live on the web; the lead disappears from open views as it does there.
      }
    } catch (e) {
      _toast(errorText(e, 'Add a name and company first, or convert it from the lead page.'), error: true);
    }
  }

  // ------------------------------------------------------------ change view

  Future<void> moveToView(List<String> ids, String label, {VoidCallback? done}) async {
    final target = await _sheet<LeadView>('Change view', (ctx) {
      final options = views.where((v) => v.key != currentViewKey).toList();
      return Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(MlSpacing.md, 0, MlSpacing.md, MlSpacing.xs),
            child: Align(
              alignment: AlignmentDirectional.centerStart,
              child: Text('Move $label to another view:',
                  style: TextStyle(fontSize: 13, color: MlColors.textMuted, decoration: TextDecoration.none)),
            ),
          ),
          for (final v in options) _row(MlIcons.folder, v.label, () => Navigator.of(ctx).pop(v)),
          const SizedBox(height: MlSpacing.sm),
        ],
      );
    });
    if (target == null) return;
    try {
      await _svc.moveToView(ids, target.key, currentViewKey);
      _toast('Moved to ${target.label}.');
      done?.call();
      await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not move the lead.'), error: true);
    }
  }

  // ------------------------------------------------------------ delete

  Future<void> deleteLead(LeadRow lead) async {
    final ok = await MlDialog.show(
      context: context,
      title: 'Delete lead?',
      message: 'Are you sure you want to delete ${lead.title}? This cannot be undone.',
      confirmLabel: 'Delete',
      isDestructive: true,
      onConfirm: () {},
    );
    if (ok != true) return;
    try {
      await _svc.deleteLead(lead.id);
      _toast('Deleted.');
      await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not delete this lead.'), error: true);
    }
  }

  // ------------------------------------------------------------ bulk

  Future<void> bulkStatus(List<String> ids, VoidCallback done) async {
    final choices = cfg.settableStatuses.where((s) => s.value != 'disqualified').toList();
    final picked = await _sheet<String>('Change status · ${ids.length} leads', (ctx) {
      return Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final s in choices) _row(MlIcons.check, s.label, () => Navigator.of(ctx).pop(s.value)),
          Padding(
            padding: const EdgeInsets.all(MlSpacing.md),
            child: Text('To mark leads as not interested, change them one at a time so each gets a reason.'.tr,
                style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none)),
          ),
        ],
      );
    });
    if (picked == null) return;
    try {
      await _svc.bulkChangeStatus(ids, picked);
      _toast('Updated ${ids.length} leads.');
      done();
      await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not update the selected leads.'), error: true);
    }
  }

  Future<void> bulkMove(List<String> ids, VoidCallback done) =>
      moveToView(ids, '${ids.length} selected lead(s)', done: done);

  Future<void> bulkDelete(List<String> ids, VoidCallback done) async {
    final ok = await MlDialog.show(
      context: context,
      title: 'Delete ${ids.length} leads?',
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      isDestructive: true,
      onConfirm: () {},
    );
    if (ok != true) return;
    try {
      await _svc.bulkDelete(ids);
      _toast('Deleted ${ids.length} leads.');
      done();
      await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not delete the selected leads.'), error: true);
    }
  }

  // ------------------------------------------------------------ create / edit

  /// Opens the lead form. Pass null to create a lead.
  Future<void> editLead(LeadRow? lead) async {
    Map<String, dynamic> raw = {};
    if (lead != null) {
      final store = ref.read(offlineStoreProvider);
      try {
        raw = await _svc.getLeadRaw(lead.id);
        store.putCache('lead-raw:${lead.id}', raw);
      } catch (e) {
        // Offline: edit the copy saved the last time this lead was opened, and queue the change.
        final saved = isOfflineError(e) ? await store.getCache('lead-raw:${lead.id}') : null;
        if (saved == null) {
          _toast(errorText(e, 'We could not open this lead.'), error: true);
          return;
        }
        raw = Map<String, dynamic>.from(saved.data as Map);
      }
    }
    String s(String k) => (raw[k] as String?) ?? '';
    final first = TextEditingController(text: s('firstName'));
    final last = TextEditingController(text: s('lastName'));
    final company = TextEditingController(text: s('companyName'));
    final email = TextEditingController(text: s('email'));
    final phone = TextEditingController(text: s('phone'));
    final address = TextEditingController(text: s('address'));
    final city = TextEditingController(text: s('city'));
    final state = TextEditingController(text: s('state'));
    final notes = TextEditingController(text: s('description'));
    String? source = raw['source'] as String?;

    final go = await _sheet<bool>(lead == null ? 'New lead' : 'Edit lead', (ctx) {
      return StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              MlTextField(label: 'Company', placeholder: 'Company name', controller: company),
              const SizedBox(height: MlSpacing.sm),
              Row(
                children: [
                  Expanded(child: MlTextField(label: 'First name', controller: first)),
                  const SizedBox(width: MlSpacing.sm),
                  Expanded(child: MlTextField(label: 'Last name', controller: last)),
                ],
              ),
              const SizedBox(height: MlSpacing.sm),
              MlTextField(label: 'Email', controller: email, keyboardType: TextInputType.emailAddress),
              const SizedBox(height: MlSpacing.sm),
              MlTextField(label: 'Phone', controller: phone, keyboardType: TextInputType.phone),
              const SizedBox(height: MlSpacing.sm),
              MlTextField(label: 'Address', controller: address),
              const SizedBox(height: MlSpacing.sm),
              Row(
                children: [
                  Expanded(child: MlTextField(label: 'City', controller: city)),
                  const SizedBox(width: MlSpacing.sm),
                  Expanded(child: MlTextField(label: 'State', controller: state)),
                ],
              ),
              if (cfg.sources.isNotEmpty) ...[
                const SizedBox(height: MlSpacing.md),
                Text('Source'.tr, style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: MlColors.textSecondary, decoration: TextDecoration.none)),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final o in cfg.sources)
                      GestureDetector(
                        onTap: () => setS(() => source = source == o.value ? null : o.value),
                        child: Container(
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                          decoration: BoxDecoration(
                            color: source == o.value ? MlColors.primary : MlColors.surfaceElevated,
                            borderRadius: BorderRadius.circular(MlRadii.full),
                          ),
                          child: Text(o.label,
                              style: TextStyle(
                                  fontSize: 12.5,
                                  fontWeight: FontWeight.w600,
                                  color: source == o.value ? const Color(0xFFFFFFFF) : MlColors.textSecondary,
                                  decoration: TextDecoration.none)),
                        ),
                      ),
                  ],
                ),
              ],
              const SizedBox(height: MlSpacing.md),
              MlTextField(label: 'Notes', controller: notes, maxLines: 4, minLines: 2),
              const SizedBox(height: MlSpacing.md),
              MlButton(label: lead == null ? 'Create lead' : 'Save changes', onPressed: () => Navigator.of(ctx).pop(true)),
            ],
          ),
        );
      });
    });
    if (go != true) return;

    if (first.text.trim().isEmpty && last.text.trim().isEmpty && company.text.trim().isEmpty) {
      _toast('Enter a name or a company.', error: true);
      return;
    }
    String? n(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();
    final body = <String, dynamic>{
      'firstName': first.text.trim(),
      'lastName': last.text.trim(),
      'email': n(email),
      'phone': n(phone),
      'companyName': n(company),
      'status': raw['status'] ?? 'new',
      'source': source,
      'rating': raw['rating'],
      'description': n(notes),
      'tags': raw['tags'] ?? <String>[],
      'ownerId': raw['ownerId'],
      'customFields': raw['customFields'] ?? <String, dynamic>{},
      'address': n(address),
      'city': n(city),
      'state': n(state),
    };
    if (lead == null) {
      final go = await LeadTools(context: context, ref: ref, cfg: cfg).confirmNotDuplicate(
        first: '${body['firstName'] ?? ''}',
        last: '${body['lastName'] ?? ''}',
        email: body['email'] as String?,
        phone: body['phone'] as String?,
        company: body['companyName'] as String?,
      );
      if (!go) return;
    }
    try {
      final sent = await sendOrQueue(
        ref,
        send: () => _svc.saveLead(lead?.id, body),
        method: lead == null ? 'POST' : 'PUT',
        path: lead == null ? '/api/v1/leads' : '/api/v1/leads/${lead.id}',
        body: body,
        label: lead == null ? 'Create lead ${body['companyName'] ?? body['firstName'] ?? ''}' : 'Edit lead ${lead.title}',
      );
      _toast(sent ? (lead == null ? 'Lead created.' : 'Lead saved.') : 'Saved on this phone. It will sync when you are back online.');
      if (sent) await onChanged();
    } catch (e) {
      _toast(errorText(e, 'We could not save this lead.'), error: true);
    }
  }
}


/// Completes when the app comes back to the foreground (after the dialer was shown), or after a minute at most.
class _ResumeOnce with WidgetsBindingObserver {
  _ResumeOnce() {
    WidgetsBinding.instance.addObserver(this);
    _t = Timer(const Duration(minutes: 30), cancel);
  }

  final _c = Completer<void>();
  bool _wasAway = false;
  Timer? _t;
  Future<void> get future => _c.future;

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.inactive) _wasAway = true;
    if (state == AppLifecycleState.resumed && _wasAway) {
      cancel();
    }
  }

  void cancel() {
    _t?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    if (!_c.isCompleted) _c.complete();
  }
}
