import 'dart:io';
import 'dart:typed_data';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../core/network_provider.dart';
import '../leads/presentation/lead_actions.dart';

String str(Object? v) => v == null ? '' : '$v';

String dateLabel(Object? iso) {
  final d = DateTime.tryParse(str(iso))?.toLocal();
  if (d == null) return '';
  const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return '${d.day} ${m[d.month - 1]} ${d.year}';
}

String timeLabel(Object? iso) {
  final d = DateTime.tryParse(str(iso))?.toLocal();
  if (d == null) return '';
  return '${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
}

String money(Object? amount, Object? currency) {
  final n = (amount as num?)?.toDouble() ?? 0;
  final s = n.round().toString().replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (m) => ',');
  return '${str(currency).isEmpty ? '' : '${str(currency)} '}$s';
}

Future<void> openUri(BuildContext context, Uri uri) async {
  try {
    if (!await launchUrl(uri, mode: LaunchMode.externalApplication) && context.mounted) {
      MlToast.show(context, message: 'No app found to open this.');
    }
  } catch (_) {
    if (context.mounted) MlToast.show(context, message: 'No app found to open this.');
  }
}

class SectionCard extends StatelessWidget {
  const SectionCard({super.key, required this.title, required this.child, this.action});
  final String title;
  final Widget child;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(bottom: MlSpacing.md),
      padding: const EdgeInsets.all(MlSpacing.md),
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
              Expanded(child: Text(title, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary))),
              if (action != null) action!,
            ],
          ),
          const SizedBox(height: MlSpacing.sm),
          child,
        ],
      ),
    );
  }
}

class InfoRow extends StatelessWidget {
  const InfoRow(this.label, this.value, {super.key, this.onTap});
  final String label;
  final String? value;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    if (value == null || value!.trim().isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 104, child: Text(label, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))),
          Expanded(
            child: GestureDetector(
              onTap: onTap,
              child: Text(value!,
                  style: MlTypography.bodyMedium.copyWith(color: onTap == null ? MlColors.textPrimary : MlColors.primary, fontWeight: FontWeight.w500)),
            ),
          ),
        ],
      ),
    );
  }
}

class ActionChip2 extends StatelessWidget {
  const ActionChip2(this.icon, this.label, this.onTap, {super.key});
  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return MlPressable(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.fromLTRB(12, 9, 16, 9),
        decoration: BoxDecoration(
          color: MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.full),
          border: Border.all(color: MlColors.border),
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          MlIcon(icon, size: 19, color: MlColors.primary),
          const SizedBox(width: 7),
          Text(label, style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600, color: MlColors.textPrimary, decoration: TextDecoration.none)),
        ]),
      ),
    );
  }
}

/// Call / WhatsApp / SMS / email in one tap, for any record with a phone or email.
class ReachChips extends StatelessWidget {
  const ReachChips({super.key, this.phone, this.email});
  final String? phone;
  final String? email;

  @override
  Widget build(BuildContext context) {
    final p = (phone ?? '').trim();
    final e = (email ?? '').trim();
    if (p.isEmpty && e.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: MlSpacing.md),
      child: Wrap(spacing: 8, runSpacing: 8, children: [
        if (p.isNotEmpty) ...[
          ActionChip2(MlIcons.call, 'Call', () => openUri(context, Uri(scheme: 'tel', path: p))),
          ActionChip2(MlIcons.whatsapp, 'WhatsApp', () => openUri(context, Uri.parse('https://wa.me/${p.replaceAll(RegExp(r'[^0-9]'), '')}'))),
          ActionChip2(MlIcons.sms, 'SMS', () => openUri(context, Uri(scheme: 'sms', path: p))),
        ],
        if (e.isNotEmpty) ActionChip2(MlIcons.email, 'Email', () => openUri(context, Uri(scheme: 'mailto', path: e))),
      ]),
    );
  }
}

class RecordTile extends StatelessWidget {
  const RecordTile({super.key, required this.title, this.subtitle, this.trailing, this.leading, this.onTap});
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final Widget? leading;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return MlPressable(
      scale: 0.98,
      onTap: onTap,
      child: Container(
        margin: const EdgeInsets.only(bottom: MlSpacing.sm),
        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
        decoration: BoxDecoration(
          color: MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.lg),
          border: Border.all(color: MlColors.border),
        ),
        child: Row(
          children: [
            if (leading != null) ...[leading!, const SizedBox(width: MlSpacing.md)],
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary)),
                  if (subtitle != null && subtitle!.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    Text(subtitle!, maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted)),
                  ],
                ],
              ),
            ),
            if (trailing != null) ...[const SizedBox(width: MlSpacing.sm), trailing!],
            if (onTap != null) MlIcon(MlIcons.chevronRight, size: 22, color: MlColors.textMuted),
          ],
        ),
      ),
    );
  }
}

// ----------------------------------------------------------------------------- avatar

final Map<String, Uint8List?> _avatarCache = {};

/// A colleague's profile photo, or their initials when they have none.
class UserAvatar extends ConsumerStatefulWidget {
  const UserAvatar({super.key, required this.userId, required this.name, this.size = MlAvatarSize.md, this.version = 0});
  final String? userId;
  final String name;
  final MlAvatarSize size;

  /// Change to make the photo load again (after the person uploads a new one).
  final int version;

  /// Forget saved photos, e.g. after changing your own.
  static void forget(String? userId) => _avatarCache.remove(userId);

  @override
  ConsumerState<UserAvatar> createState() => _UserAvatarState();
}

class _UserAvatarState extends ConsumerState<UserAvatar> {
  Uint8List? _bytes;

  double get _px => switch (widget.size) {
        MlAvatarSize.sm => 32,
        MlAvatarSize.md => 40,
        MlAvatarSize.lg => 56,
        _ => 40,
      };

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(covariant UserAvatar old) {
    super.didUpdateWidget(old);
    if (old.userId != widget.userId || old.version != widget.version) _load();
  }

  Future<void> _load() async {
    final id = widget.userId;
    if (id == null || id.isEmpty) return;
    if (_avatarCache.containsKey(id) && widget.version == 0) {
      if (mounted) setState(() => _bytes = _avatarCache[id]);
      return;
    }
    try {
      final data = await ref.read(workspaceServiceProvider).avatar(id);
      _avatarCache[id] = data == null ? null : Uint8List.fromList(data);
      if (mounted) setState(() => _bytes = _avatarCache[id]);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    if (_bytes == null) return MlAvatar(name: widget.name, size: widget.size);
    return ClipOval(child: Image.memory(_bytes!, width: _px, height: _px, fit: BoxFit.cover, gaplessPlayback: true));
  }
}

// ----------------------------------------------------------------------------- documents

/// Files attached to a record: see them, open them, add a photo or document.
class DocumentsSection extends ConsumerStatefulWidget {
  const DocumentsSection({super.key, required this.entityType, required this.entityId, this.canAdd = true});

  /// leads, contacts, accounts or deals.
  final String entityType;
  final String entityId;
  final bool canAdd;

  @override
  ConsumerState<DocumentsSection> createState() => _DocumentsSectionState();
}

class _DocumentsSectionState extends ConsumerState<DocumentsSection> {
  List<Map<String, dynamic>>? _files;
  bool _failed = false;
  bool _uploading = false;
  String? _opening;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final f = await ref.read(workspaceServiceProvider).files(widget.entityType, widget.entityId);
      if (mounted) setState(() {
        _files = f;
        _failed = false;
      });
    } catch (_) {
      if (mounted) setState(() => _failed = true);
    }
  }

  Future<void> _upload(String path, String name) async {
    setState(() => _uploading = true);
    try {
      await ref.read(workspaceServiceProvider).uploadFile(widget.entityType, widget.entityId, path: path, name: name);
      await _load();
      if (mounted) MlToast.show(context, message: 'Document added.', type: MlToastType.success);
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not upload the document.'), type: MlToastType.error);
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  void _addSheet() {
    MlSheet.show<void>(
      context: context,
      title: 'Add document',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          MlButton(label: 'Camera', icon: MlIcon(MlIcons.camera, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
            Navigator.of(sheetContext).pop();
            _pickImage(ImageSource.camera);
          }),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'Photo from gallery', icon: MlIcon(MlIcons.gallery, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
            Navigator.of(sheetContext).pop();
            _pickImage(ImageSource.gallery);
          }),
          const SizedBox(height: MlSpacing.sm),
          MlButton(label: 'File (PDF, Word, Excel, text)', icon: MlIcon(MlIcons.document, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
            Navigator.of(sheetContext).pop();
            _pickFile();
          }),
        ]),
      ),
    );
  }

  Future<void> _pickImage(ImageSource source) async {
    try {
      final x = await ImagePicker().pickImage(source: source, imageQuality: 85, maxWidth: 2400);
      if (x != null) await _upload(x.path, x.name.isEmpty ? 'photo.jpg' : x.name);
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not open the camera or gallery.');
    }
  }

  Future<void> _pickFile() async {
    try {
      final r = await FilePicker.platform.pickFiles(type: FileType.custom, allowedExtensions: ['pdf', 'docx', 'xlsx', 'txt', 'csv']);
      final f = r?.files.single;
      if (f?.path != null) await _upload(f!.path!, f.name);
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not pick the file.');
    }
  }

  Future<void> _open(Map<String, dynamic> f) async {
    final id = str(f['id']);
    setState(() => _opening = id);
    try {
      final bytes = await ref.read(workspaceServiceProvider).downloadFile(id);
      final dir = await getTemporaryDirectory();
      final safe = str(f['fileName']).replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
      final file = File('${dir.path}/doc_${id}_$safe');
      await file.writeAsBytes(bytes);
      final res = await OpenFilex.open(file.path);
      if (res.type != ResultType.done && mounted) MlToast.show(context, message: 'No app found to open this file.');
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not open the document.'), type: MlToastType.error);
    } finally {
      if (mounted) setState(() => _opening = null);
    }
  }

  IconData _iconFor(String name) {
    final n = name.toLowerCase();
    if (n.endsWith('.jpg') || n.endsWith('.jpeg') || n.endsWith('.png') || n.endsWith('.webp')) return MlIcons.gallery;
    return MlIcons.document;
  }

  @override
  Widget build(BuildContext context) {
    final files = _files;
    return SectionCard(
      title: 'Documents',
      action: widget.canAdd
          ? (_uploading
              ? const MlSpinner(size: 20)
              : MlPressable(
                  onTap: _addSheet,
                  child: Row(children: [
                    const MlIcon(MlIcons.add, size: 20, color: MlColors.primary),
                    Text('Add', style: MlTypography.labelMedium.copyWith(color: MlColors.primary)),
                  ]),
                ))
          : null,
      child: files == null
          ? (_failed
              ? Text('Could not load documents.', style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))
              : const Center(child: Padding(padding: EdgeInsets.all(8), child: MlSpinner(size: 22))))
          : files.isEmpty
              ? Text('No documents yet.', style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))
              : Column(
                  children: [
                    for (final f in files)
                      MlPressable(
                        scale: 0.99,
                        onTap: _opening == null ? () => _open(f) : null,
                        child: Padding(
                          padding: const EdgeInsets.symmetric(vertical: 8),
                          child: Row(children: [
                            Container(
                              width: 40,
                              height: 40,
                              decoration: BoxDecoration(color: MlColors.primary.withOpacity(0.12), borderRadius: BorderRadius.circular(12)),
                              alignment: Alignment.center,
                              child: _opening == str(f['id']) ? const MlSpinner(size: 18) : MlIcon(_iconFor(str(f['fileName'])), size: 22, color: MlColors.primary),
                            ),
                            const SizedBox(width: MlSpacing.md),
                            Expanded(
                              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                                Text(str(f['fileName']), maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                                Text('${_size((f['sizeBytes'] as num?)?.toInt())} · ${dateLabel(f['createdAtUtc'])}', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                              ]),
                            ),
                          ]),
                        ),
                      ),
                  ],
                ),
    );
  }

  static String _size(int? b) {
    if (b == null) return '';
    if (b < 1024) return '$b B';
    if (b < 1024 * 1024) return '${(b / 1024).toStringAsFixed(0)} KB';
    return '${(b / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
}

// ----------------------------------------------------------------------------- activities

/// The tasks, calls and notes linked to a record. Tap an open one to complete it.
class LinkedActivities extends ConsumerStatefulWidget {
  const LinkedActivities({super.key, required this.field, required this.id});

  /// leadId, contactId, accountId or dealId.
  final String field;
  final String id;

  @override
  ConsumerState<LinkedActivities> createState() => _LinkedActivitiesState();
}

class _LinkedActivitiesState extends ConsumerState<LinkedActivities> {
  List<Map<String, dynamic>>? _items;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final p = await ref.read(workspaceServiceProvider).activitiesFor(widget.field, widget.id);
      if (mounted) setState(() => _items = p.items);
    } catch (_) {
      if (mounted) setState(() => _items = const []);
    }
  }

  Future<void> _complete(Map<String, dynamic> a) async {
    try {
      await ref.read(workspaceServiceProvider).completeActivity(str(a['id']));
      await _load();
      if (mounted) MlToast.show(context, message: '${str(a['type'])} marked complete.', type: MlToastType.success);
    } catch (e) {
      if (mounted) MlToast.show(context, message: LeadActions.errorText(e, 'Could not complete this.'), type: MlToastType.error);
    }
  }

  static IconData _icon(String type) => switch (type) {
        'Call' => MlIcons.call,
        'Note' => MlIcons.note,
        'Meeting' => MlIcons.handshake,
        _ => MlIcons.checkCircle,
      };

  @override
  Widget build(BuildContext context) {
    final items = _items;
    return SectionCard(
      title: 'Activity',
      child: items == null
          ? const Center(child: Padding(padding: EdgeInsets.all(8), child: MlSpinner(size: 22)))
          : items.isEmpty
              ? Text('Nothing logged yet.', style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted))
              : Column(children: [
                  for (final a in items.take(12))
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 7),
                      child: Row(children: [
                        MlIcon(_icon(str(a['type'])), size: 22, color: str(a['status']) == 'Completed' ? MlColors.success : MlColors.primary),
                        const SizedBox(width: MlSpacing.md),
                        Expanded(
                          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                            Text(str(a['subject']), maxLines: 1, overflow: TextOverflow.ellipsis, style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary)),
                            Text('${str(a['type'])} · ${dateLabel(a['dueAtUtc'])} ${timeLabel(a['dueAtUtc'])}', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                          ]),
                        ),
                        if (str(a['status']) != 'Completed')
                          MlButton(label: 'Done', size: MlButtonSize.small, variant: MlButtonVariant.secondary, onPressed: () => _complete(a)),
                      ]),
                    ),
                ]),
    );
  }
}
