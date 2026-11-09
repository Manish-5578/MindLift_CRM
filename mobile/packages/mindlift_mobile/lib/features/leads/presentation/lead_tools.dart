import 'dart:io';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';
import '../../../core/network_provider.dart';
import '../../workspace/workspace_widgets.dart' show str;
import 'lead_actions.dart';

/// Export, import, duplicate merge and the full convert dialog: what the web leads list and lead page offer beyond the basics.
class LeadTools {
  LeadTools({required this.context, required this.ref, required this.cfg});
  final BuildContext context;
  final WidgetRef ref;
  final LeadWorkspaceConfig cfg;

  LeadsService get _svc => ref.read(leadsServiceProvider);

  void _toast(String m, {bool error = false}) {
    if (context.mounted) MlToast.show(context, message: m, type: error ? MlToastType.error : MlToastType.success);
  }

  Future<void> _openBytes(List<int> bytes, String name) async {
    final dir = await getTemporaryDirectory();
    final file = File('${dir.path}/$name');
    await file.writeAsBytes(bytes);
    final res = await OpenFilex.open(file.path);
    if (res.type != ResultType.done) _toast('Saved as $name, but no app was found to open it.', error: true);
  }

  // ------------------------------------------------------------------ menu

  void menu({required LeadView view, required String search, required Future<void> Function() onImported}) {
    MlSheet.show<void>(
      context: context,
      title: 'More',
      builder: (ctx) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (cfg.canExport) ...[
            MlButton(label: 'Export this view (CSV)', icon: const MlIcon(MlIcons.document, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
              Navigator.of(ctx).pop();
              exportView(view, search);
            }),
            const SizedBox(height: MlSpacing.sm),
          ],
          if (cfg.canImport) ...[
            MlButton(label: 'Import leads from a file', icon: const MlIcon(MlIcons.attach, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
              Navigator.of(ctx).pop();
              importFile(onImported);
            }),
            const SizedBox(height: MlSpacing.sm),
            MlButton(label: 'Get the import template', icon: const MlIcon(MlIcons.list, size: 22), variant: MlButtonVariant.ghost, onPressed: () {
              Navigator.of(ctx).pop();
              template();
            }),
          ],
          if (!cfg.canExport && !cfg.canImport)
            Text('Your role cannot export or import leads.'.tr, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted)),
        ]),
      ),
    );
  }

  // ------------------------------------------------------------------ export / import

  Future<void> exportView(LeadView view, String search) async {
    try {
      _toast('Preparing the export…');
      final bytes = await _svc.exportCsv(search: search, filter: view.filter, sortField: view.sortField ?? 'created_at', sortDir: view.sortDir ?? 'desc');
      await _openBytes(bytes, 'leads-${view.key.replaceAll(RegExp(r'[^A-Za-z0-9]'), '')}.csv');
    } catch (e) {
      _toast(LeadActions.errorText(e, 'We could not export the leads.'), error: true);
    }
  }

  Future<void> template() async {
    try {
      await _openBytes(await _svc.importTemplateCsv(), 'leads-template.csv');
    } catch (e) {
      _toast(LeadActions.errorText(e, 'We could not get the template.'), error: true);
    }
  }

  Future<void> importFile(Future<void> Function() onImported) async {
    try {
      final r = await FilePicker.platform.pickFiles(type: FileType.custom, allowedExtensions: ['csv', 'xlsx']);
      final f = r?.files.single;
      if (f?.path == null) return;
      _toast('Importing ${f!.name}…');
      final result = await _svc.importFile(path: f.path!, name: f.name);
      final errors = ((result['errors'] as List?) ?? const []).map((e) => '$e').toList();
      await onImported();
      if (!context.mounted) return;
      await MlSheet.show<void>(
        context: context,
        title: 'Import finished',
        builder: (ctx) => Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Row(children: [
              _stat('${result['created'] ?? 0}', 'Added', MlColors.success),
              const SizedBox(width: MlSpacing.sm),
              _stat('${result['skipped'] ?? 0}', 'Skipped', MlColors.warning),
              const SizedBox(width: MlSpacing.sm),
              _stat('${result['failed'] ?? 0}', 'Failed', MlColors.danger),
            ]),
            if (errors.isNotEmpty) ...[
              const SizedBox(height: MlSpacing.md),
              Text('What went wrong'.tr, style: MlTypography.labelMedium.copyWith(color: MlColors.textSecondary)),
              const SizedBox(height: 4),
              for (final e in errors.take(8)) Padding(padding: const EdgeInsets.only(bottom: 4), child: Text('• $e', style: MlTypography.bodySmall.copyWith(color: MlColors.textPrimary))),
              if (errors.length > 8) Text('and ${errors.length - 8} more', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
            ],
            const SizedBox(height: MlSpacing.md),
            MlButton(label: 'Done', onPressed: () => Navigator.of(ctx).pop()),
          ]),
        ),
      );
    } catch (e) {
      _toast(LeadActions.errorText(e, 'We could not import that file.'), error: true);
    }
  }

  Widget _stat(String n, String label, Color color) => Expanded(
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 12),
          decoration: BoxDecoration(color: color.withOpacity(0.12), borderRadius: BorderRadius.circular(MlRadii.lg)),
          child: Column(children: [
            Text(n, style: MlTypography.headingMedium.copyWith(color: color, fontWeight: FontWeight.w800)),
            Text(label, style: MlTypography.caption.copyWith(color: MlColors.textSecondary)),
          ]),
        ),
      );

  // ------------------------------------------------------------------ duplicates

  Future<void> duplicates(LeadRow lead, Future<void> Function() onMerged) async {
    List<Map<String, dynamic>> found;
    try {
      found = await _svc.duplicatesOf(lead.id);
    } catch (e) {
      _toast(LeadActions.errorText(e, 'We could not look for duplicates.'), error: true);
      return;
    }
    if (!context.mounted) return;
    if (found.isEmpty) {
      _toast('No duplicates found for ${lead.title}.');
      return;
    }
    final leadsOnly = found.where((d) => str(d['kind']).toLowerCase() == 'lead').toList();
    final selected = <String>{for (final d in leadsOnly) str(d['id'])};
    await MlSheet.show<void>(
      context: context,
      title: 'Possible duplicates',
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: const EdgeInsets.all(MlSpacing.md),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            for (final d in found)
              MlPressable(
                scale: 0.99,
                onTap: str(d['kind']).toLowerCase() == 'lead' ? () => setS(() => selected.contains(str(d['id'])) ? selected.remove(str(d['id'])) : selected.add(str(d['id']))) : null,
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Row(children: [
                    if (str(d['kind']).toLowerCase() == 'lead')
                      MlIcon(selected.contains(str(d['id'])) ? MlIcons.selectOn : MlIcons.selectOff, size: 24, color: selected.contains(str(d['id'])) ? MlColors.primary : MlColors.textMuted)
                    else
                      MlIcon(MlIcons.person, size: 24, color: MlColors.textMuted),
                    const SizedBox(width: MlSpacing.md),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text('${str(d['fullName'])}  ·  ${str(d['kind'])}', style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                        Text([str(d['email']), str(d['phone']), str(d['reason'])].where((e) => e.isNotEmpty).join(' · '), style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                      ]),
                    ),
                  ]),
                ),
              ),
            const SizedBox(height: MlSpacing.xs),
            if (leadsOnly.isNotEmpty && cfg.canDelete)
              MlButton(
                label: 'Merge ${selected.length} into this lead',
                onPressed: selected.isEmpty
                    ? null
                    : () async {
                        Navigator.of(ctx).pop();
                        try {
                          await _svc.merge(lead.id, selected.toList());
                          _toast('Merged. The duplicates were folded into ${lead.title}.');
                          await onMerged();
                        } catch (e) {
                          _toast(LeadActions.errorText(e, 'We could not merge them.'), error: true);
                        }
                      },
              )
            else
              Text(cfg.canDelete ? 'Contacts and accounts are only listed; they are not merged from here.' : 'Merging needs delete permission.', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
          ]),
        );
      }),
    );
  }

  /// Before a new lead is saved: if it looks like someone already in the CRM, ask first. Returns true to go ahead.
  Future<bool> confirmNotDuplicate({required String first, required String last, String? email, String? phone, String? company}) async {
    List<Map<String, dynamic>> found;
    try {
      found = await _svc.checkDuplicates(firstName: first, lastName: last, email: email, phone: phone, companyName: company);
    } catch (_) {
      return true; // never block saving because the check could not run (offline, for example)
    }
    if (found.isEmpty || !context.mounted) return true;
    final go = await MlSheet.show<bool>(
      context: context,
      title: 'Already in your CRM?',
      builder: (ctx) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('These look like the same person or company:'.tr, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted)),
          const SizedBox(height: MlSpacing.sm),
          for (final d in found.take(5))
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Text('${str(d['fullName'])} (${str(d['kind'])}) · ${str(d['reason'])}', style: MlTypography.bodyMedium.copyWith(color: MlColors.textPrimary)),
            ),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'Save anyway', onPressed: () => Navigator.of(ctx).pop(true)),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'Go back', variant: MlButtonVariant.ghost, onPressed: () => Navigator.of(ctx).pop(false)),
        ]),
      ),
    );
    return go == true;
  }

  // ------------------------------------------------------------------ convert

  Future<void> convert(LeadRow lead, Future<void> Function() onConverted) async {
    Map<String, dynamic> options;
    try {
      options = await _svc.convertOptions(lead.id);
    } catch (e) {
      _toast(LeadActions.errorText(e, 'We could not open the convert options.'), error: true);
      return;
    }
    if (!context.mounted) return;
    await MlSheet.show<void>(
      context: context,
      title: 'Convert lead',
      builder: (ctx) => _ConvertForm(
        lead: lead,
        options: options,
        onSubmit: (body) async {
          final result = await _svc.convert(lead.id, body);
          if (ctx.mounted) Navigator.of(ctx).pop();
          _toast(result['dealId'] != null ? 'Converted to a contact, account and deal.' : 'Converted.');
          await onConverted();
        },
      ),
    );
  }
}

class _ConvertForm extends StatefulWidget {
  const _ConvertForm({required this.lead, required this.options, required this.onSubmit});
  final LeadRow lead;
  final Map<String, dynamic> options;
  final Future<void> Function(Map<String, dynamic> body) onSubmit;

  @override
  State<_ConvertForm> createState() => _ConvertFormState();
}

class _ConvertFormState extends State<_ConvertForm> {
  late String _accountMode = (widget.options['companyName'] ?? widget.lead.companyName) == null ? 'none' : 'create';
  String? _accountId;
  String _contactMode = 'create';
  String? _contactId;
  bool _deal = false;
  late final _accountName = TextEditingController(text: str(widget.options['companyName'] ?? widget.lead.companyName));
  late final _dealName = TextEditingController(text: '${widget.lead.title} deal');
  final _amount = TextEditingController();
  bool _busy = false;

  List<Map<String, dynamic>> _list(String key) => ((widget.options[key] as List?) ?? const []).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();

  @override
  void dispose() {
    _accountName.dispose();
    _dealName.dispose();
    _amount.dispose();
    super.dispose();
  }

  Widget _choice(String label, bool on, VoidCallback tap) => MlPressable(
        onTap: tap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Row(children: [
            MlIcon(on ? MlIcons.radioOn : MlIcons.selectOff, size: 24, color: on ? MlColors.primary : MlColors.textMuted),
            const SizedBox(width: 10),
            Expanded(child: Text(label, style: MlTypography.bodyMedium.copyWith(color: MlColors.textPrimary))),
          ]),
        ),
      );

  Future<void> _go() async {
    if (_accountMode == 'existing' && _accountId == null) {
      MlToast.show(context, message: 'Choose which account to use.', type: MlToastType.error);
      return;
    }
    if (_contactMode == 'existing' && _contactId == null) {
      MlToast.show(context, message: 'Choose which contact to use.', type: MlToastType.error);
      return;
    }
    // A new deal goes into the first open stage of the default pipeline.
    final pipelines = _list('availablePipelines');
    final pipeline = pipelines.firstWhere((p) => p['isDefault'] == true, orElse: () => pipelines.isEmpty ? <String, dynamic>{} : pipelines.first);
    final stages = ((pipeline['stages'] as List?) ?? const []).whereType<Map>().where((s) => s['stageType'] == 'Open').toList()
      ..sort((a, b) => ((a['sortOrder'] as num?) ?? 0).compareTo((b['sortOrder'] as num?) ?? 0));
    if (_deal && (pipeline.isEmpty || stages.isEmpty)) {
      MlToast.show(context, message: 'Create a pipeline in the web app before adding a deal.', type: MlToastType.error);
      return;
    }
    setState(() => _busy = true);
    try {
      await widget.onSubmit({
        'accountMode': _accountMode,
        'existingAccountId': _accountMode == 'existing' ? _accountId : null,
        'accountName': _accountMode == 'create' ? _accountName.text.trim() : null,
        'contactMode': _contactMode,
        'existingContactId': _contactMode == 'existing' ? _contactId : null,
        'createDeal': _deal,
        'dealName': _deal ? _dealName.text.trim() : null,
        'pipelineId': _deal ? pipeline['id'] : null,
        'stageId': _deal ? stages.first['id'] : null,
        'amount': _deal ? double.tryParse(_amount.text.trim()) : null,
        'currency': _deal ? widget.options['baseCurrency'] : null,
        'expectedCloseDate': null,
      });
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        MlToast.show(context, message: LeadActions.errorText(e, 'We could not convert this lead.'), type: MlToastType.error);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final accounts = _list('matchingAccounts');
    final contacts = _list('matchingContacts');
    return Padding(
      padding: const EdgeInsets.all(MlSpacing.md),
      child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('Account'.tr, style: MlTypography.labelMedium.copyWith(color: MlColors.textSecondary)),
        _choice('Create a new account', _accountMode == 'create', () => setState(() => _accountMode = 'create')),
        if (_accountMode == 'create') MlTextField(placeholder: 'Company name', controller: _accountName),
        for (final a in accounts) _choice('Use ${str(a['title'])}${str(a['subtitle']).isEmpty ? '' : ' · ${str(a['subtitle'])}'}', _accountMode == 'existing' && _accountId == str(a['id']), () => setState(() {
              _accountMode = 'existing';
              _accountId = str(a['id']);
            })),
        _choice('No account', _accountMode == 'none', () => setState(() => _accountMode = 'none')),
        const SizedBox(height: MlSpacing.sm),
        Text('Contact'.tr, style: MlTypography.labelMedium.copyWith(color: MlColors.textSecondary)),
        _choice('Create a new contact', _contactMode == 'create', () => setState(() => _contactMode = 'create')),
        for (final c in contacts) _choice('Use ${str(c['title'])}${str(c['subtitle']).isEmpty ? '' : ' · ${str(c['subtitle'])}'}', _contactMode == 'existing' && _contactId == str(c['id']), () => setState(() {
              _contactMode = 'existing';
              _contactId = str(c['id']);
            })),
        const SizedBox(height: MlSpacing.sm),
        MlPressable(
          onTap: () => setState(() => _deal = !_deal),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Row(children: [
              MlIcon(_deal ? MlIcons.selectOn : MlIcons.selectOff, size: 24, color: _deal ? MlColors.primary : MlColors.textMuted),
              const SizedBox(width: 10),
              Expanded(child: Text('Also create a deal'.tr, style: MlTypography.bodyMedium.copyWith(color: MlColors.textPrimary))),
            ]),
          ),
        ),
        if (_deal) ...[
          MlTextField(placeholder: 'Deal name', controller: _dealName),
          const SizedBox(height: MlSpacing.sm),
          MlTextField(placeholder: 'Amount (${str(widget.options['baseCurrency'])})', controller: _amount, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
        ],
        const SizedBox(height: MlSpacing.md),
        MlButton(label: 'Convert', isLoading: _busy, onPressed: _busy ? null : _go),
      ]),
    );
  }
}
