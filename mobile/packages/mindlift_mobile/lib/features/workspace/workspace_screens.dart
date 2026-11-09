import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../core/network_provider.dart';
import '../../core/offline_store.dart';
import '../leads/presentation/lead_actions.dart';
import 'record_detail_screen.dart';
import 'record_list_screen.dart';
import 'workspace_widgets.dart';

/// YYYY-MM-DD to an ISO date the API accepts, or null if it is not a date (empty stays null too).
String? _parseDate(String text) {
  final t = text.trim();
  if (t.isEmpty) return null;
  final d = DateTime.tryParse(t);
  return d == null ? null : DateTime.utc(d.year, d.month, d.day).toIso8601String();
}

MlStatusState _dealState(String status) => switch (status.toLowerCase()) {
      'won' => MlStatusState.success,
      'lost' => MlStatusState.danger,
      _ => MlStatusState.primary,
    };

/// A small form in a sheet: labelled fields, a save button, errors shown in a toast.
Future<bool> _formSheet(
  BuildContext context, {
  required String title,
  required List<({String label, String hint, TextInputTypeHint type, String? initial})> fields,
  required Future<void> Function(List<String> values) onSave,
}) async {
  var saved = false;
  await MlSheet.show<void>(
    context: context,
    title: title,
    builder: (sheetContext) => _FormBody(
      fields: fields,
      onSave: (values) async {
        await onSave(values);
        saved = true;
        if (sheetContext.mounted) Navigator.of(sheetContext).pop();
      },
    ),
  );
  return saved;
}

enum TextInputTypeHint { text, phone, email, number }

class _FormBody extends StatefulWidget {
  const _FormBody({required this.fields, required this.onSave});
  final List<({String label, String hint, TextInputTypeHint type, String? initial})> fields;
  final Future<void> Function(List<String> values) onSave;

  @override
  State<_FormBody> createState() => _FormBodyState();
}

class _FormBodyState extends State<_FormBody> {
  late final List<TextEditingController> _c = [for (final f in widget.fields) TextEditingController(text: f.initial)];
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in _c) {
      c.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(MlSpacing.md),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, mainAxisSize: MainAxisSize.min, children: [
        for (var i = 0; i < widget.fields.length; i++) ...[
          MlTextField(
            label: widget.fields[i].label,
            placeholder: widget.fields[i].hint,
            controller: _c[i],
            keyboardType: switch (widget.fields[i].type) {
              TextInputTypeHint.phone => TextInputType.phone,
              TextInputTypeHint.email => TextInputType.emailAddress,
              TextInputTypeHint.number => const TextInputType.numberWithOptions(decimal: true),
              _ => TextInputType.text,
            },
            errorText: i == 0 ? _error : null,
          ),
          const SizedBox(height: MlSpacing.sm),
        ],
        const SizedBox(height: MlSpacing.xs),
        MlButton(
          label: 'Save',
          isLoading: _busy,
          onPressed: _busy
              ? null
              : () async {
                  if (_c.first.text.trim().isEmpty) {
                    setState(() => _error = '${widget.fields.first.label} is required.');
                    return;
                  }
                  setState(() {
                    _busy = true;
                    _error = null;
                  });
                  try {
                    await widget.onSave([for (final c in _c) c.text.trim()]);
                  } catch (e) {
                    if (mounted) {
                      setState(() => _busy = false);
                      MlToast.show(context, message: LeadActions.errorText(e, 'Could not save.'), type: MlToastType.error);
                    }
                  }
                },
        ),
      ]),
    );
  }
}

// ============================================================================= contacts

class ContactsScreen extends StatelessWidget {
  const ContactsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return RecordListScreen(
      title: 'Contacts',
      entity: 'contacts',
      searchHint: 'Search name, email, phone or company',
      emptyTitle: 'No contacts yet',
      emptyIcon: MlIcons.person,
      onAdd: (context, ref) async {
        await _formSheet(context, title: 'New contact', fields: [
          (label: 'First name', hint: 'Rita', type: TextInputTypeHint.text, initial: null),
          (label: 'Last name', hint: 'Shaw', type: TextInputTypeHint.text, initial: null),
          (label: 'Phone', hint: '+971 50 123 4567', type: TextInputTypeHint.phone, initial: null),
          (label: 'Email', hint: 'rita@company.com', type: TextInputTypeHint.email, initial: null),
          (label: 'Job title', hint: 'Purchasing manager', type: TextInputTypeHint.text, initial: null),
        ], onSave: (v) async {
          await ref.read(workspaceServiceProvider).createContact(firstName: v[0], lastName: v[1], phone: v[2].isEmpty ? null : v[2], email: v[3].isEmpty ? null : v[3], jobTitle: v[4].isEmpty ? null : v[4]);
          if (context.mounted) MlToast.show(context, message: 'Contact created.', type: MlToastType.success);
        });
      },
      rowBuilder: (context, c) => RecordTile(
        leading: MlAvatar(name: str(c['fullName']), size: MlAvatarSize.md),
        title: str(c['fullName']).isEmpty ? 'Contact' : str(c['fullName']),
        subtitle: [str(c['jobTitle']), str(c['accountName'])].where((e) => e.isNotEmpty).join(' · '),
        onTap: () => context.push('/contacts/${c['id']}'),
      ),
    );
  }
}

class ContactDetailScreen extends ConsumerWidget {
  const ContactDetailScreen({super.key, required this.id});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return RecordDetailScreen(
      entity: 'contacts',
      id: id,
      title: (s) => str((s['contact'] as Map)['firstName']) + ' ' + str((s['contact'] as Map)['lastName']),
      subtitle: (s) => [str((s['contact'] as Map)['jobTitle']), str((s['contact'] as Map)['accountName'])].where((e) => e.isNotEmpty).join(' · '),
      actions: (s, reload) => [
        MlIconButton(label: 'Edit contact', icon: const MlIcon(MlIcons.edit, size: 24), onPressed: () async {
          final c = Map<String, dynamic>.from(s['contact'] as Map);
          final ok = await _formSheet(context, title: 'Edit contact', fields: [
            (label: 'First name', hint: 'Rita', type: TextInputTypeHint.text, initial: str(c['firstName'])),
            (label: 'Last name', hint: 'Shaw', type: TextInputTypeHint.text, initial: str(c['lastName'])),
            (label: 'Phone', hint: '+971 50 123 4567', type: TextInputTypeHint.phone, initial: str(c['phone'])),
            (label: 'Email', hint: 'rita@company.com', type: TextInputTypeHint.email, initial: str(c['email'])),
            (label: 'Job title', hint: 'Purchasing manager', type: TextInputTypeHint.text, initial: str(c['jobTitle'])),
            (label: 'Notes', hint: 'Anything worth remembering', type: TextInputTypeHint.text, initial: str(c['description'])),
          ], onSave: (v) async {
            await ref.read(workspaceServiceProvider).updateContact(c, firstName: v[0], lastName: v[1], phone: v[2].isEmpty ? null : v[2], email: v[3].isEmpty ? null : v[3], jobTitle: v[4].isEmpty ? null : v[4], description: v[5].isEmpty ? null : v[5]);
          });
          if (ok) {
            if (context.mounted) MlToast.show(context, message: 'Contact updated.', type: MlToastType.success);
            await reload();
          }
        }),
      ],
      builder: (context, s, reload) {
        final c = Map<String, dynamic>.from(s['contact'] as Map);
        final deals = ((s['deals'] as List?) ?? const []).whereType<Map>().toList();
        return [
          ReachChips(phone: str(c['phone']), email: str(c['email'])),
          SectionCard(title: 'Details', child: Column(children: [
            InfoRow('Phone', str(c['phone'])),
            InfoRow('Email', str(c['email'])),
            InfoRow('Job title', str(c['jobTitle'])),
            InfoRow('Company', str(c['accountName']), onTap: str(c['accountId']).isEmpty ? null : () => context.push('/accounts/${c['accountId']}')),
            InfoRow('Type', str(c['type'])),
            InfoRow('Added', dateLabel(c['createdAtUtc'])),
            InfoRow('Notes', str(c['description'])),
          ])),
          if (deals.isNotEmpty)
            SectionCard(title: 'Deals', child: Column(children: [
              for (final d in deals)
                RecordTile(
                  title: str(d['name']),
                  subtitle: money(d['amount'], d['currency']),
                  trailing: MlStatusChip(label: str(d['status']), state: _dealState(str(d['status']))),
                  onTap: () => context.push('/deals/${d['id']}'),
                ),
            ])),
          LinkedActivities(field: 'contactId', id: id),
          DocumentsSection(entityType: 'contacts', entityId: id),
        ];
      },
    );
  }
}

// ============================================================================= accounts

class AccountsScreen extends StatelessWidget {
  const AccountsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return RecordListScreen(
      title: 'Accounts',
      entity: 'accounts',
      searchHint: 'Search companies',
      emptyTitle: 'No accounts yet',
      emptyIcon: MlIcons.leadsOutline,
      onAdd: (context, ref) async {
        await _formSheet(context, title: 'New account', fields: [
          (label: 'Company name', hint: 'Acme Logistics', type: TextInputTypeHint.text, initial: null),
          (label: 'Phone', hint: '+971 4 123 4567', type: TextInputTypeHint.phone, initial: null),
          (label: 'Website', hint: 'acme.com', type: TextInputTypeHint.text, initial: null),
          (label: 'Industry', hint: 'Logistics', type: TextInputTypeHint.text, initial: null),
        ], onSave: (v) async {
          await ref.read(workspaceServiceProvider).createAccount(name: v[0], phone: v[1].isEmpty ? null : v[1], website: v[2].isEmpty ? null : v[2], industry: v[3].isEmpty ? null : v[3]);
          if (context.mounted) MlToast.show(context, message: 'Account created.', type: MlToastType.success);
        });
      },
      rowBuilder: (context, a) => RecordTile(
        leading: MlAvatar(name: str(a['name']), size: MlAvatarSize.md),
        title: str(a['name']),
        subtitle: [str(a['industry']), '${a['contactCount'] ?? 0} contacts'].where((e) => e.isNotEmpty).join(' · '),
        onTap: () => context.push('/accounts/${a['id']}'),
      ),
    );
  }
}

class AccountDetailScreen extends ConsumerWidget {
  const AccountDetailScreen({super.key, required this.id});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return RecordDetailScreen(
      entity: 'accounts',
      id: id,
      title: (s) => str((s['account'] as Map)['name']),
      subtitle: (s) => str((s['account'] as Map)['industry']),
      actions: (s, reload) => [
        MlIconButton(label: 'Edit account', icon: const MlIcon(MlIcons.edit, size: 24), onPressed: () async {
          final a = Map<String, dynamic>.from(s['account'] as Map);
          final ok = await _formSheet(context, title: 'Edit account', fields: [
            (label: 'Company name', hint: 'Acme Logistics', type: TextInputTypeHint.text, initial: str(a['name'])),
            (label: 'Phone', hint: '+971 4 123 4567', type: TextInputTypeHint.phone, initial: str(a['phone'])),
            (label: 'Website', hint: 'acme.com', type: TextInputTypeHint.text, initial: str(a['website'])),
            (label: 'Industry', hint: 'Logistics', type: TextInputTypeHint.text, initial: str(a['industry'])),
            (label: 'Notes', hint: 'Anything worth remembering', type: TextInputTypeHint.text, initial: str(a['description'])),
          ], onSave: (v) async {
            await ref.read(workspaceServiceProvider).updateAccount(a, name: v[0], phone: v[1].isEmpty ? null : v[1], website: v[2].isEmpty ? null : v[2], industry: v[3].isEmpty ? null : v[3], description: v[4].isEmpty ? null : v[4]);
          });
          if (ok) {
            if (context.mounted) MlToast.show(context, message: 'Account updated.', type: MlToastType.success);
            await reload();
          }
        }),
      ],
      builder: (context, s, reload) {
        final a = Map<String, dynamic>.from(s['account'] as Map);
        final contacts = ((s['contacts'] as List?) ?? const []).whereType<Map>().toList();
        final deals = ((s['deals'] as List?) ?? const []).whereType<Map>().toList();
        final website = str(a['website']);
        return [
          ReachChips(phone: str(a['phone'])),
          SectionCard(title: 'Details', child: Column(children: [
            InfoRow('Phone', str(a['phone'])),
            InfoRow('Website', website, onTap: website.isEmpty ? null : () => openUri(context, Uri.parse(website.startsWith('http') ? website : 'https://$website'))),
            InfoRow('Industry', str(a['industry'])),
            InfoRow('Parent', str(a['parentName'])),
            InfoRow('Open deals', money(s['openDealsAmount'], '')),
            InfoRow('Won deals', money(s['wonDealsAmount'], '')),
            InfoRow('Added', dateLabel(a['createdAtUtc'])),
            InfoRow('Notes', str(a['description'])),
          ])),
          if (contacts.isNotEmpty)
            SectionCard(title: 'Contacts', child: Column(children: [
              for (final c in contacts)
                RecordTile(
                  leading: MlAvatar(name: str(c['fullName'] ?? c['name']), size: MlAvatarSize.sm),
                  title: str(c['fullName'] ?? c['name']),
                  subtitle: str(c['jobTitle'] ?? c['email']),
                  onTap: () => context.push('/contacts/${c['id'] ?? c['contactId']}'),
                ),
            ])),
          if (deals.isNotEmpty)
            SectionCard(title: 'Deals', child: Column(children: [
              for (final d in deals)
                RecordTile(
                  title: str(d['name']),
                  subtitle: money(d['amount'], d['currency']),
                  trailing: MlStatusChip(label: str(d['status']), state: _dealState(str(d['status']))),
                  onTap: () => context.push('/deals/${d['id']}'),
                ),
            ])),
          LinkedActivities(field: 'accountId', id: id),
          DocumentsSection(entityType: 'accounts', entityId: id),
        ];
      },
    );
  }
}

// ============================================================================= deals

class DealsScreen extends StatelessWidget {
  const DealsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return RecordListScreen(
      title: 'Deals',
      entity: 'deals',
      searchHint: 'Search deals or companies',
      emptyTitle: 'No deals yet',
      emptyIcon: MlIcons.handshake,
      onAdd: (context, ref) async {
        await _formSheet(context, title: 'New deal', fields: [
          (label: 'Deal name', hint: 'Acme rollout', type: TextInputTypeHint.text, initial: null),
          (label: 'Amount', hint: '12000', type: TextInputTypeHint.number, initial: null),
          (label: 'Currency', hint: 'AED', type: TextInputTypeHint.text, initial: null),
          (label: 'Expected close (YYYY-MM-DD)', hint: '2026-12-31', type: TextInputTypeHint.text, initial: null),
        ], onSave: (v) async {
          final close = _parseDate(v[3]);
          if (v[3].isNotEmpty && close == null) throw const FormatException('Enter the date as YYYY-MM-DD.');
          await ref.read(workspaceServiceProvider).createDeal(name: v[0], amount: double.tryParse(v[1]) ?? 0, currency: v[2].isEmpty ? null : v[2].toUpperCase(), expectedCloseDate: close);
          if (context.mounted) MlToast.show(context, message: 'Deal created.', type: MlToastType.success);
        });
      },
      rowBuilder: (context, d) => RecordTile(
        title: str(d['name']),
        subtitle: [str(d['accountName']), money(d['amount'], d['currency'])].where((e) => e.isNotEmpty).join(' · '),
        trailing: MlStatusChip(label: str(d['stageName']).isEmpty ? str(d['status']) : str(d['stageName']), state: _dealState(str(d['status']))),
        onTap: () => context.push('/deals/${d['id']}'),
      ),
    );
  }
}

class DealDetailScreen extends ConsumerWidget {
  const DealDetailScreen({super.key, required this.id});
  final String id;

  Future<void> _changeStage(BuildContext context, WidgetRef ref, Map<String, dynamic> deal, Future<void> Function() reload) async {
    List<Map<String, dynamic>> stages = [];
    try {
      final pipelines = await ref.read(workspaceServiceProvider).pipelines();
      final p = pipelines.firstWhere((p) => str(p['id']) == str(deal['pipelineId']), orElse: () => const {});
      stages = ((p['stages'] as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList()
        ..sort((a, b) => ((a['sortOrder'] as num?) ?? 0).compareTo((b['sortOrder'] as num?) ?? 0));
    } catch (e) {
      if (context.mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not load the stages.'), type: MlToastType.error);
      return;
    }
    if (!context.mounted) return;
    final reason = TextEditingController();
    final picked = await MlSheet.show<Map<String, dynamic>>(
      context: context,
      title: 'Move to stage',
      builder: (ctx) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          for (final s in stages)
            MlPressable(
              scale: 0.98,
              onTap: str(s['id']) == str(deal['stageId']) ? null : () => Navigator.of(ctx).pop(s),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 12),
                child: Row(children: [
                  MlIcon(str(s['id']) == str(deal['stageId']) ? MlIcons.radioOn : MlIcons.selectOff, size: 24, color: str(s['id']) == str(deal['stageId']) ? MlColors.primary : MlColors.textMuted),
                  const SizedBox(width: 12),
                  Expanded(child: Text(str(s['name']), style: MlTypography.bodyLarge.copyWith(color: MlColors.textPrimary))),
                  Text('${s['probability']}%', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                ]),
              ),
            ),
          const SizedBox(height: 4),
          MlTextField(label: 'Reason, if you are marking it lost', placeholder: 'Optional', controller: reason),
        ]),
      ),
    );
    if (picked == null) return;
    try {
      final isLost = str(picked['stageType']).toLowerCase() == 'lost';
      final sent = await sendOrQueue(
        ref,
        send: () => ref.read(workspaceServiceProvider).changeDealStage(id, str(picked['id']), lostReason: isLost && reason.text.trim().isNotEmpty ? reason.text.trim() : null),
        method: 'PATCH',
        path: '/api/v1/deals/$id/stage',
        body: {'stageId': str(picked['id']), if (isLost && reason.text.trim().isNotEmpty) 'lostReason': reason.text.trim()},
        label: 'Move ${str(deal['name'])} to ${str(picked['name'])}',
      );
      if (context.mounted) MlToast.show(context, message: sent ? 'Moved to ${str(picked['name'])}.' : 'Saved on this phone. It will sync when you are back online.', type: MlToastType.success);
      if (sent) await reload();
    } catch (e) {
      if (context.mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not move the deal.'), type: MlToastType.error);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return RecordDetailScreen(
      entity: 'deals',
      id: id,
      title: (d) => str(d['name']),
      subtitle: (d) => money(d['amount'], d['currency']),
      actions: (d, reload) => [
        MlIconButton(label: 'Edit deal', icon: const MlIcon(MlIcons.edit, size: 24), onPressed: () async {
          final ok = await _formSheet(context, title: 'Edit deal', fields: [
            (label: 'Deal name', hint: 'Acme rollout', type: TextInputTypeHint.text, initial: str(d['name'])),
            (label: 'Amount', hint: '12000', type: TextInputTypeHint.number, initial: ((d['amount'] as num?) ?? 0).toString()),
            (label: 'Currency', hint: 'AED', type: TextInputTypeHint.text, initial: str(d['currency'])),
            (label: 'Expected close (YYYY-MM-DD)', hint: '2026-12-31', type: TextInputTypeHint.text, initial: str(d['expectedCloseDate']).split('T').first),
            (label: 'Notes', hint: 'Anything worth remembering', type: TextInputTypeHint.text, initial: str(d['description'])),
          ], onSave: (v) async {
            final close = _parseDate(v[3]);
            if (v[3].isNotEmpty && close == null) throw const FormatException('Enter the date as YYYY-MM-DD.');
            await ref.read(workspaceServiceProvider).updateDeal(d, name: v[0], amount: double.tryParse(v[1]) ?? 0, currency: v[2].isEmpty ? null : v[2].toUpperCase(), expectedCloseDate: close, description: v[4].isEmpty ? null : v[4]);
          });
          if (ok) {
            if (context.mounted) MlToast.show(context, message: 'Deal updated.', type: MlToastType.success);
            await reload();
          }
        }),
      ],
      builder: (context, d, reload) {
        final contacts = ((d['contacts'] as List?) ?? const []).whereType<Map>().toList();
        return [
          SectionCard(
            title: 'Deal',
            action: MlStatusChip(label: str(d['status']), state: _dealState(str(d['status']))),
            child: Column(children: [
              Text(money(d['amount'], d['currency']), style: MlTypography.headingLarge.copyWith(color: MlColors.textPrimary, fontWeight: FontWeight.w800)),
              const SizedBox(height: MlSpacing.sm),
              InfoRow('Probability', '${d['probability']}%'),
              InfoRow('Expected close', dateLabel(d['expectedCloseDate'])),
              InfoRow('Company', str(d['accountName']), onTap: str(d['accountId']).isEmpty ? null : () => context.push('/accounts/${d['accountId']}')),
              InfoRow('Forecast', str(d['forecastCategory'])),
              InfoRow('Lost because', str(d['lostReason'])),
              InfoRow('Notes', str(d['description'])),
              const SizedBox(height: MlSpacing.xs),
              MlButton(label: 'Change stage', icon: MlIcon(MlIcons.swap, size: 20, color: MlColors.white), onPressed: () => _changeStage(context, ref, d, reload)),
            ]),
          ),
          if (contacts.isNotEmpty)
            SectionCard(title: 'People', child: Column(children: [
              for (final c in contacts)
                RecordTile(
                  leading: MlAvatar(name: str(c['fullName']), size: MlAvatarSize.sm),
                  title: str(c['fullName']),
                  subtitle: str(c['role']),
                  onTap: () => context.push('/contacts/${c['contactId']}'),
                ),
            ])),
          LinkedActivities(field: 'dealId', id: id),
          DocumentsSection(entityType: 'deals', entityId: id),
        ];
      },
    );
  }
}
