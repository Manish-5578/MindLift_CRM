import 'dart:async';
import 'dart:io';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/services.dart' show Clipboard, ClipboardData, HapticFeedback, TextInputAction;
import 'package:image_picker/image_picker.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../../core/top_bar_actions.dart';
import 'chat_widgets.dart';

const _white = Color(0xFFFFFFFF);

String _hm(DateTime utc) {
  final t = utc.toLocal();
  return '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
}

String _day(DateTime utc) {
  final t = utc.toLocal();
  final now = DateTime.now();
  final diff = DateTime(now.year, now.month, now.day).difference(DateTime(t.year, t.month, t.day)).inDays;
  if (diff == 0) return 'Today';
  if (diff == 1) return 'Yesterday';
  const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return '${t.day} ${m[t.month - 1]} ${t.year}';
}

/// Short label for the conversation list: time today, "Yesterday", or the date.
String _listStamp(DateTime utc) {
  final d = _day(utc);
  return d == 'Today' ? _hm(utc) : d;
}

/// Direct, person-to-person messaging between colleagues of the same company (no public channels).
class ChatScreen extends ConsumerStatefulWidget {
  const ChatScreen({super.key});

  @override
  ConsumerState<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends ConsumerState<ChatScreen> {
  ChatUserDto? _open;
  List<ChatUserDto> _people = [];
  bool _loadingPeople = true;
  String _query = '';
  Timer? _listTimer;

  @override
  void initState() {
    super.initState();
    _loadPeople();
    _listTimer = Timer.periodic(const Duration(seconds: 8), (_) {
      if (mounted && _open == null) _loadPeople();
    });
  }

  @override
  void dispose() {
    _listTimer?.cancel();
    super.dispose();
  }

  Future<void> _loadPeople() async {
    try {
      final list = await ref.read(chatServiceProvider).getUsers();
      if (mounted) setState(() => _people = list);
    } catch (_) {
    } finally {
      if (mounted && _loadingPeople) setState(() => _loadingPeople = false);
    }
  }

  void _close() {
    setState(() => _open = null);
    _loadPeople();
  }

  @override
  Widget build(BuildContext context) {
    final open = _open;
    if (open != null) {
      return _ThreadView(key: ValueKey(open.id), person: open, onBack: _close);
    }
    final q = _query.trim().toLowerCase();
    final shown = q.isEmpty
        ? _people
        : _people.where((p) => p.name.toLowerCase().contains(q) || (p.role ?? '').toLowerCase().contains(q)).toList();
    final totalUnread = _people.fold<int>(0, (a, p) => a + p.unreadCount);
    final onlineCount = _people.where((p) => p.isOnline).length;

    return Container(
      color: MlColors.background,
      child: Column(
        children: [
          // Gradient header
          Container(
            width: double.infinity,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [MlColors.primary, const Color(0xFF3730A3)],
              ),
            ),
            child: SafeArea(
              bottom: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(MlSpacing.md, MlSpacing.md, MlSpacing.md, MlSpacing.md),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        const Expanded(
                          child: Text('Messages',
                              style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: _white, decoration: TextDecoration.none)),
                        ),
                        const ThemeIconButton(color: _white),
                        const BellButton(),
                        if (totalUnread > 0)
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                            decoration: BoxDecoration(color: const Color(0x33FFFFFF), borderRadius: BorderRadius.circular(MlRadii.full)),
                            child: Text('$totalUnread unread',
                                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: _white, decoration: TextDecoration.none)),
                          ),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text('$onlineCount of ${_people.length} colleagues online',
                        style: const TextStyle(fontSize: 13, color: Color(0xCCFFFFFF), decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                    const SizedBox(height: MlSpacing.md),
                    Container(
                      decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.full)),
                      padding: const EdgeInsets.symmetric(horizontal: 6),
                      child: MlTextField(
                        placeholder: 'Search people',
                        prefix: MlIcon(MlIcons.search, size: 20, color: MlColors.textMuted),
                        onChanged: (v) => setState(() => _query = v),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
          Expanded(
            child: _loadingPeople
                ? const Center(child: Text('Loading...'))
                : shown.isEmpty
                    ? Center(
                        child: Text(
                          _people.isEmpty ? 'No colleagues to chat with yet.' : 'No matches.',
                          style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
                        ),
                      )
                    : ListView.builder(
                        padding: const EdgeInsets.only(top: 6, bottom: 12),
                        itemCount: shown.length,
                        itemBuilder: (context, i) => _PersonTile(person: shown[i], onTap: () => setState(() => _open = shown[i])),
                      ),
          ),
        ],
      ),
    );
  }
}

class _PersonTile extends StatelessWidget {
  const _PersonTile({required this.person, required this.onTap});
  final ChatUserDto person;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final unread = person.unreadCount > 0;
    final preview = person.lastMessage ?? (person.role ?? 'Team member');
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: MlSpacing.sm, vertical: 3),
        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
        decoration: BoxDecoration(
          color: MlColors.surface,
          borderRadius: BorderRadius.circular(MlRadii.lg),
          border: Border.all(color: unread ? MlColors.primary.withOpacity(0.35) : MlColors.border),
        ),
        child: Row(
          children: [
            Stack(
              children: [
                MlAvatar(name: person.name, size: MlAvatarSize.lg),
                Positioned(
                  right: 0,
                  bottom: 0,
                  child: Container(
                    width: 13,
                    height: 13,
                    decoration: BoxDecoration(
                      color: person.isOnline ? MlColors.success : MlColors.textDisabled,
                      shape: BoxShape.circle,
                      border: Border.all(color: MlColors.surface, width: 2),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(width: MlSpacing.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(person.name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                                fontSize: 16,
                                fontWeight: unread ? FontWeight.w800 : FontWeight.w600,
                                color: MlColors.textPrimary,
                                decoration: TextDecoration.none)),
                      ),
                      if (person.lastMessageAtUtc != null)
                        Text(_listStamp(person.lastMessageAtUtc!),
                            style: TextStyle(
                                fontSize: 11,
                                color: unread ? MlColors.primary : MlColors.textMuted,
                                fontWeight: unread ? FontWeight.w700 : FontWeight.w400,
                                decoration: TextDecoration.none)),
                    ],
                  ),
                  const SizedBox(height: 3),
                  Row(
                    children: [
                      Expanded(
                        child: Text(preview,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                                fontSize: 13,
                                color: unread ? MlColors.textPrimary : MlColors.textMuted,
                                fontWeight: unread ? FontWeight.w600 : FontWeight.w400,
                                decoration: TextDecoration.none)),
                      ),
                      if (unread) ...[
                        const SizedBox(width: 8),
                        Container(
                          constraints: const BoxConstraints(minWidth: 22),
                          padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
                          decoration: BoxDecoration(color: MlColors.primary, borderRadius: BorderRadius.circular(MlRadii.full)),
                          child: Text('${person.unreadCount}',
                              textAlign: TextAlign.center,
                              style: const TextStyle(fontSize: 11, color: _white, fontWeight: FontWeight.w800, decoration: TextDecoration.none)),
                        ),
                      ],
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ThreadView extends ConsumerStatefulWidget {
  const _ThreadView({super.key, required this.person, required this.onBack});
  final ChatUserDto person;
  final VoidCallback onBack;

  @override
  ConsumerState<_ThreadView> createState() => _ThreadViewState();
}

class _ThreadViewState extends ConsumerState<_ThreadView> {
  final _controller = TextEditingController();
  final _searchCtl = TextEditingController();
  final _scroll = ScrollController();
  final _recorder = AudioRecorder();
  List<ChatMessageDto> _messages = [];
  bool _loading = true;
  bool _hasText = false;
  late bool _online = widget.person.isOnline;
  bool _typing = false;
  DateTime _typingSentAt = DateTime.fromMillisecondsSinceEpoch(0);
  Timer? _poll;

  ChatMessageDto? _replyTo;
  bool _searching = false;
  String _searchTerm = '';
  List<ChatMessageDto> _results = [];
  bool _uploading = false;
  int _newBelow = 0;
  bool _atBottom = true;

  bool _recording = false;
  DateTime? _recStart;
  Timer? _recTimer;
  int _recMs = 0;

  static const _quickReactions = ['👍', '❤️', '😂', '😮', '🙏', '✅'];

  @override
  void initState() {
    super.initState();
    _controller.addListener(() {
      final has = _controller.text.trim().isNotEmpty;
      if (has != _hasText) setState(() => _hasText = has);
      if (has && DateTime.now().difference(_typingSentAt).inSeconds >= 3) {
        _typingSentAt = DateTime.now();
        ref.read(chatServiceProvider).typing(widget.person.id).catchError((_) {});
      }
    });
    _scroll.addListener(() {
      if (!_scroll.hasClients) return;
      final at = _scroll.position.maxScrollExtent - _scroll.offset < 120;
      if (at != _atBottom) setState(() => _atBottom = at);
      if (at && _newBelow != 0) setState(() => _newBelow = 0);
    });
    _fetch(initial: true);
    _poll = Timer.periodic(const Duration(seconds: 3), (_) {
      if (mounted && !_searching) _fetch();
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    _recTimer?.cancel();
    _recorder.dispose();
    _controller.dispose();
    _searchCtl.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _toBottom({bool animate = true}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) return;
      final max = _scroll.position.maxScrollExtent;
      if (animate) {
        _scroll.animateTo(max, duration: const Duration(milliseconds: 200), curve: Curves.easeOut);
      } else {
        _scroll.jumpTo(max);
      }
    });
  }

  static String _signature(List<ChatMessageDto> l) =>
      l.map((m) => '${m.id}${m.isRead}${m.reactions.map((r) => '${r.emoji}${r.count}${r.mine}').join()}').join('|');

  Future<void> _fetch({bool initial = false}) async {
    try {
      final svc = ref.read(chatServiceProvider);
      final list = await svc.getMessages(recipientId: widget.person.id);
      if (!mounted) return;
      final changed = _signature(list) != _signature(_messages.where((m) => !m.id.startsWith('local-')).toList());
      if (changed || initial) {
        final added = list.length - _messages.where((m) => !m.id.startsWith('local-')).length;
        final atBottom = !_scroll.hasClients || _scroll.position.maxScrollExtent - _scroll.offset < 100;
        setState(() {
          _messages = [...list, ..._messages.where((m) => m.id.startsWith('local-'))];
          if (!initial && !atBottom && added > 0) _newBelow += added;
        });
        if (initial) {
          _toBottom(animate: false);
        } else if (atBottom) {
          _toBottom();
        }
        svc.markRead(otherUserId: widget.person.id).catchError((_) {});
      }
      final typing = await svc.isTyping(widget.person.id).catchError((_) => false);
      if (mounted && typing != _typing) setState(() => _typing = typing);
      // Keep the online dot fresh.
      if (initial || DateTime.now().second % 15 < 3) {
        final users = await svc.getUsers();
        final me = users.where((u) => u.id == widget.person.id);
        if (me.isNotEmpty && mounted && me.first.isOnline != _online) setState(() => _online = me.first.isOnline);
      }
    } catch (_) {
    } finally {
      if (mounted && _loading) setState(() => _loading = false);
    }
  }

  Future<void> _send() async {
    final text = _controller.text.trim();
    if (text.isEmpty) return;
    final reply = _replyTo;
    _controller.clear();
    setState(() => _replyTo = null);
    // Optimistic bubble so the message appears instantly.
    final temp = ChatMessageDto(
      id: 'local-${DateTime.now().microsecondsSinceEpoch}',
      senderId: 'me',
      senderName: 'You',
      senderEmail: ref.read(authNotifierProvider).userEmail,
      recipientId: widget.person.id,
      message: text,
      createdAtUtc: DateTime.now().toUtc(),
      isRead: false,
      replyToId: reply?.id,
      replyToText: reply == null ? null : _preview(reply),
      replyToSender: reply?.senderName,
    );
    setState(() => _messages = [..._messages, temp]);
    _toBottom();
    try {
      final sent = await ref.read(chatServiceProvider).sendMessage(message: text, recipientId: widget.person.id, replyToId: reply?.id);
      HapticFeedback.lightImpact();
      if (mounted) setState(() => _messages = [..._messages.where((m) => m.id != temp.id), sent]);
    } catch (_) {
      if (mounted) {
        setState(() => _messages = _messages.where((m) => m.id != temp.id).toList());
        _controller.text = text;
        MlToast.show(context, message: 'Could not send. Check your connection.');
      }
    }
  }

  static String _preview(ChatMessageDto m) {
    if (m.message.trim().isNotEmpty) return m.message;
    return switch (m.attachmentKind) {
      'image' => '📷 Photo',
      'audio' => '🎤 Voice message',
      'file' => '📎 ${m.attachmentName ?? 'File'}',
      _ => '',
    };
  }

  Future<void> _sendFile(String path, String name, {String? caption, int? durationMs}) async {
    final reply = _replyTo;
    setState(() {
      _uploading = true;
      _replyTo = null;
    });
    try {
      final sent = await ref.read(chatServiceProvider).sendAttachment(
            filePath: path,
            fileName: name,
            recipientId: widget.person.id,
            caption: caption,
            replyToId: reply?.id,
            durationMs: durationMs,
          );
      HapticFeedback.lightImpact();
      if (mounted) {
        setState(() => _messages = [..._messages, sent]);
        _toBottom();
      }
    } catch (e) {
      if (mounted) MlToast.show(context, message: 'Could not send the file. It may be too large (15 MB max) or an unsupported type.', type: MlToastType.error);
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  void _attachSheet() {
    MlSheet.show<void>(
      context: context,
      title: 'Send',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
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
            MlButton(label: 'Document', icon: MlIcon(MlIcons.document, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
              Navigator.of(sheetContext).pop();
              _pickDocument();
            }),
          ],
        ),
      ),
    );
  }

  Future<void> _pickImage(ImageSource source) async {
    try {
      final x = await ImagePicker().pickImage(source: source, imageQuality: 80, maxWidth: 1920);
      if (x == null) return;
      final caption = _controller.text.trim();
      _controller.clear();
      await _sendFile(x.path, x.name.isEmpty ? 'photo.jpg' : x.name, caption: caption);
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not open the camera or gallery.');
    }
  }

  Future<void> _pickDocument() async {
    try {
      final r = await FilePicker.platform.pickFiles(type: FileType.custom, allowedExtensions: ['pdf', 'docx', 'xlsx', 'txt', 'csv']);
      final f = r?.files.single;
      if (f == null || f.path == null) return;
      await _sendFile(f.path!, f.name);
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not pick the file.');
    }
  }

  Future<void> _startRecording() async {
    try {
      if (!await _recorder.hasPermission()) {
        if (mounted) MlToast.show(context, message: 'Allow microphone access to send voice messages.');
        return;
      }
      final dir = await getTemporaryDirectory();
      final path = '${dir.path}/voice_${DateTime.now().millisecondsSinceEpoch}.m4a';
      await _recorder.start(const RecordConfig(encoder: AudioEncoder.aacLc, bitRate: 64000, sampleRate: 22050), path: path);
      HapticFeedback.mediumImpact();
      _recStart = DateTime.now();
      _recTimer = Timer.periodic(const Duration(milliseconds: 250), (_) {
        if (mounted && _recStart != null) setState(() => _recMs = DateTime.now().difference(_recStart!).inMilliseconds);
      });
      setState(() {
        _recording = true;
        _recMs = 0;
      });
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not start recording.');
    }
  }

  Future<void> _finishRecording({required bool send}) async {
    _recTimer?.cancel();
    final ms = _recStart == null ? 0 : DateTime.now().difference(_recStart!).inMilliseconds;
    setState(() => _recording = false);
    String? path;
    try {
      path = await _recorder.stop();
    } catch (_) {}
    if (!send || path == null) {
      if (path != null) File(path).delete().catchError((_) => File(path!));
      return;
    }
    if (ms < 700) {
      MlToast.show(context, message: 'Hold on a little longer to record.');
      return;
    }
    await _sendFile(path, path.split(RegExp(r'[\\/]')).last, durationMs: ms);
  }

  void _messageMenu(ChatMessageDto m) {
    if (m.id.startsWith('local-')) return;
    HapticFeedback.selectionClick();
    MlSheet.show<void>(
      context: context,
      title: 'Message',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                for (final e in _quickReactions)
                  GestureDetector(
                    onTap: () {
                      Navigator.of(sheetContext).pop();
                      _react(m, e);
                    },
                    child: Container(
                      padding: const EdgeInsets.all(8),
                      decoration: BoxDecoration(color: MlColors.surfaceMuted, shape: BoxShape.circle),
                      child: Text(e, style: const TextStyle(fontSize: 24, decoration: TextDecoration.none)),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: MlSpacing.md),
            MlButton(label: 'Reply', icon: MlIcon(MlIcons.reply, size: 22), variant: MlButtonVariant.secondary, onPressed: () {
              Navigator.of(sheetContext).pop();
              setState(() => _replyTo = m);
            }),
            if (m.message.trim().isNotEmpty) ...[
              const SizedBox(height: MlSpacing.sm),
              MlButton(label: 'Copy text', icon: MlIcon(MlIcons.copy, size: 20), variant: MlButtonVariant.secondary, onPressed: () {
                Navigator.of(sheetContext).pop();
                Clipboard.setData(ClipboardData(text: m.message));
                MlToast.show(context, message: 'Copied.');
              }),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> _react(ChatMessageDto m, String emoji) async {
    try {
      final updated = await ref.read(chatServiceProvider).react(m.id, emoji);
      if (mounted) setState(() => _messages = [for (final x in _messages) x.id == m.id ? updated : x]);
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not react.');
    }
  }

  Future<void> _runSearch(String term) async {
    _searchTerm = term.trim();
    if (_searchTerm.isEmpty) {
      setState(() => _results = []);
      return;
    }
    try {
      final r = await ref.read(chatServiceProvider).getMessages(recipientId: widget.person.id, search: _searchTerm);
      if (mounted && _searchTerm == term.trim()) setState(() => _results = r);
    } catch (_) {}
  }

  Widget _bubble(ChatMessageDto msg, bool isMe, bool sameAsPrev, Color mine) {
    final pending = msg.id.startsWith('local-');
    final hasText = msg.message.trim().isNotEmpty;
    return Column(
      crossAxisAlignment: isMe ? CrossAxisAlignment.end : CrossAxisAlignment.start,
      children: [
        GestureDetector(
          onLongPress: () => _messageMenu(msg),
          onDoubleTap: () => msg.id.startsWith('local-') ? null : _react(msg, '👍'),
          child: Container(
            margin: EdgeInsets.only(bottom: 3, top: sameAsPrev ? 0 : 6),
            constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.78),
            padding: const EdgeInsets.fromLTRB(13, 9, 11, 6),
            decoration: BoxDecoration(
              color: isMe ? mine : MlColors.surface,
              borderRadius: BorderRadius.only(
                topLeft: const Radius.circular(18),
                topRight: const Radius.circular(18),
                bottomLeft: Radius.circular(isMe ? 18 : 4),
                bottomRight: Radius.circular(isMe ? 4 : 18),
              ),
              border: isMe ? null : Border.all(color: MlColors.border),
              boxShadow: const [BoxShadow(color: Color(0x14000000), blurRadius: 6, offset: Offset(0, 2))],
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              mainAxisSize: MainAxisSize.min,
              children: [
                if (msg.replyToId != null)
                  Align(
                    alignment: Alignment.centerLeft,
                    child: ReplyQuote(sender: msg.replyToSender ?? 'Message', text: msg.replyToText ?? '', isMe: isMe),
                  ),
                if (msg.hasAttachment)
                  Padding(
                    padding: EdgeInsets.only(bottom: hasText ? 6 : 0),
                    child: AttachmentView(message: msg, isMe: isMe),
                  ),
                if (hasText)
                  Align(
                    alignment: Alignment.centerLeft,
                    child: Text(msg.message,
                        style: TextStyle(
                            fontSize: 15.5,
                            height: 1.3,
                            color: isMe ? _white : MlColors.textPrimary,
                            decoration: TextDecoration.none,
                            fontWeight: FontWeight.w400)),
                  ),
                const SizedBox(height: 3),
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(_hm(msg.createdAtUtc),
                        style: TextStyle(
                            fontSize: 10.5,
                            decoration: TextDecoration.none,
                            fontWeight: FontWeight.w400,
                            color: isMe ? const Color(0xB3FFFFFF) : MlColors.textMuted)),
                    if (isMe) ...[
                      const SizedBox(width: 4),
                      Icon(pending ? MlIcons.pending : (msg.isRead ? MlIcons.read : MlIcons.sent),
                          size: 15, color: msg.isRead ? const Color(0xFF7DD3FC) : const Color(0xB3FFFFFF)),
                    ],
                  ],
                ),
              ],
            ),
          ),
        ),
        if (msg.reactions.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 4, left: 6, right: 6),
            child: ReactionsRow(reactions: msg.reactions, onTap: (e) => _react(msg, e)),
          ),
      ],
    );
  }

  Widget _header(ChatUserDto p) {
    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [MlColors.primary, const Color(0xFF3730A3)],
        ),
        boxShadow: const [BoxShadow(color: Color(0x33000000), blurRadius: 10, offset: Offset(0, 3))],
      ),
      child: SafeArea(
        bottom: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(4, 8, MlSpacing.sm, 10),
          child: _searching
              ? Row(
                  children: [
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: () => setState(() {
                        _searching = false;
                        _searchCtl.clear();
                        _results = [];
                        _searchTerm = '';
                      }),
                      child: const Padding(
                        padding: EdgeInsets.symmetric(horizontal: 12, vertical: 2),
                        child: MlIcon(MlIcons.back, size: 28, color: _white),
                      ),
                    ),
                    Expanded(
                      child: Container(
                        decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.full)),
                        padding: const EdgeInsets.symmetric(horizontal: 6),
                        child: MlTextField(placeholder: 'Search this chat', prefix: MlIcon(MlIcons.search, size: 20, color: MlColors.textMuted), controller: _searchCtl, onChanged: _runSearch),
                      ),
                    ),
                  ],
                )
              : Row(
                  children: [
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: widget.onBack,
                      child: const Padding(
                        padding: EdgeInsets.symmetric(horizontal: 12, vertical: 2),
                        child: MlIcon(MlIcons.back, size: 28, color: _white),
                      ),
                    ),
                    MlAvatar(name: p.name, size: MlAvatarSize.md, backgroundColor: const Color(0x33FFFFFF), textColor: _white),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(p.name,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: _white, decoration: TextDecoration.none)),
                          Row(
                            children: [
                              Container(
                                width: 8,
                                height: 8,
                                decoration: BoxDecoration(color: _online ? const Color(0xFF4ADE80) : const Color(0xFF94A3B8), shape: BoxShape.circle),
                              ),
                              const SizedBox(width: 6),
                              Flexible(
                                child: Text(_typing ? 'typing…' : (_online ? 'Online' : (p.role ?? 'Offline')),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                        fontSize: 12,
                                        color: _typing ? const Color(0xFF86EFAC) : const Color(0xCCFFFFFF),
                                        decoration: TextDecoration.none,
                                        fontWeight: _typing ? FontWeight.w700 : FontWeight.w400)),
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: () => setState(() => _searching = true),
                      child: const Padding(
                        padding: EdgeInsets.all(10),
                        child: MlIcon(MlIcons.search, size: 18),
                      ),
                    ),
                    const ThemeIconButton(color: _white),
                  ],
                ),
        ),
      ),
    );
  }

  Widget _composer() {
    if (_recording) {
      return Container(
        padding: const EdgeInsets.fromLTRB(10, 8, 10, 10),
        decoration: BoxDecoration(color: MlColors.surface, border: Border(top: BorderSide(color: MlColors.border))),
        child: SafeArea(
          top: false,
          child: Row(
            children: [
              GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => _finishRecording(send: false),
                child: const Padding(padding: EdgeInsets.all(10), child: MlIcon(MlIcons.delete, size: 22)),
              ),
              const SizedBox(width: 6),
              Container(width: 10, height: 10, decoration: BoxDecoration(color: MlColors.danger, shape: BoxShape.circle)),
              const SizedBox(width: 8),
              Expanded(
                child: Text('Recording…  ${clock(_recMs)}',
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: MlColors.textPrimary, decoration: TextDecoration.none)),
              ),
              GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => _finishRecording(send: true),
                child: Container(
                  width: 50,
                  height: 50,
                  decoration: BoxDecoration(gradient: LinearGradient(colors: [MlColors.primary, const Color(0xFF3730A3)]), shape: BoxShape.circle),
                  alignment: Alignment.center,
                  child: MlIcon(MlIcons.send, size: 20, color: _white),
                ),
              ),
            ],
          ),
        ),
      );
    }

    return Container(
      padding: const EdgeInsets.fromLTRB(6, 8, 10, 10),
      decoration: BoxDecoration(color: MlColors.surface, border: Border(top: BorderSide(color: MlColors.border))),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_replyTo != null)
              Container(
                margin: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                child: Row(
                  children: [
                    Expanded(
                      child: ReplyQuote(sender: _replyTo!.senderName, text: _preview(_replyTo!), isMe: false),
                    ),
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: () => setState(() => _replyTo = null),
                      child: Padding(
                        padding: const EdgeInsets.all(8),
                        child: MlIcon(MlIcons.close, size: 18, color: MlColors.textMuted),
                      ),
                    ),
                  ],
                ),
              ),
            if (_uploading)
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: Text('Sending…', style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
              ),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTap: _uploading ? null : _attachSheet,
                  child: const Padding(
                    padding: EdgeInsets.symmetric(horizontal: 10, vertical: 12),
                    child: MlIcon(MlIcons.attach, size: 22),
                  ),
                ),
                Expanded(
                  child: Container(
                    decoration: BoxDecoration(color: MlColors.surfaceElevated, borderRadius: BorderRadius.circular(26)),
                    padding: const EdgeInsets.symmetric(horizontal: 4),
                    child: MlTextField(
                      placeholder: 'Type a message',
                      controller: _controller,
                      maxLines: 5,
                      minLines: 1,
                      textInputAction: TextInputAction.send,
                      onEditingComplete: _send,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTap: _hasText ? _send : (_uploading ? null : _startRecording),
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 150),
                    width: 50,
                    height: 50,
                    decoration: BoxDecoration(
                      gradient: LinearGradient(colors: [MlColors.primary, const Color(0xFF3730A3)]),
                      shape: BoxShape.circle,
                      boxShadow: const [BoxShadow(color: Color(0x554F46E5), blurRadius: 10, offset: Offset(0, 4))],
                    ),
                    alignment: Alignment.center,
                    child: AnimatedSwitcher(
                      duration: const Duration(milliseconds: 180),
                      transitionBuilder: (c, a) => ScaleTransition(scale: a, child: c),
                      child: Icon(_hasText ? MlIcons.send : MlIcons.mic, key: ValueKey(_hasText), size: 24, color: _white),
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final p = widget.person;
    final myEmail = ref.watch(authNotifierProvider).userEmail ?? '';
    final dark = MlColors.isDark;
    final mine = dark ? const Color(0xFF4338CA) : MlColors.primary;
    final wallpaper = dark ? const Color(0xFF0B1120) : const Color(0xFFEEF0FA);
    final shown = _searching ? _results : _messages;

    return Container(
      color: wallpaper,
      child: Column(
        children: [
          _header(p),
          if (_searching && _searchTerm.isNotEmpty)
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 6),
              color: MlColors.surface,
              child: Text('${_results.length} result${_results.length == 1 ? '' : 's'}',
                  style: TextStyle(fontSize: 12, color: MlColors.textMuted, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
            ),
          Expanded(
            child: Stack(
              children: [
                _loading && _messages.isEmpty
                    ? const Center(child: Text('Loading...'))
                    : shown.isEmpty
                        ? Center(
                            child: Container(
                              padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 16),
                              decoration: BoxDecoration(
                                color: MlColors.surface,
                                borderRadius: BorderRadius.circular(MlRadii.xl),
                                border: Border.all(color: MlColors.border),
                              ),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  MlIcon(_searching ? MlIcons.search : MlIcons.chatOutline, size: 40, color: MlColors.textMuted),
                                  const SizedBox(height: 6),
                                  Text(_searching ? 'No messages found' : 'Say hi to ${p.name.split(' ').first}',
                                      style: MlTypography.bodyMedium.copyWith(color: MlColors.textSecondary, fontWeight: FontWeight.w600)),
                                ],
                              ),
                            ),
                          )
                        : ListView.builder(
                            controller: _scroll,
                            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
                            itemCount: shown.length,
                            itemBuilder: (context, i) {
                              final msg = shown[i];
                              final isMe = msg.senderEmail == myEmail || msg.senderName == 'You';
                              final showDay = i == 0 || _day(shown[i - 1].createdAtUtc) != _day(msg.createdAtUtc);
                              final sameAsPrev = i > 0 && !showDay && shown[i - 1].senderId == msg.senderId;
                              return RepaintBoundary(
                                child: Column(
                                  children: [
                                    if (showDay)
                                      Container(
                                        margin: const EdgeInsets.symmetric(vertical: 10),
                                        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 5),
                                        decoration: BoxDecoration(
                                          color: MlColors.surface,
                                          borderRadius: BorderRadius.circular(MlRadii.full),
                                          border: Border.all(color: MlColors.border),
                                        ),
                                        child: Text(_day(msg.createdAtUtc),
                                            style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: MlColors.textMuted, decoration: TextDecoration.none)),
                                      ),
                                    Align(
                                      alignment: isMe ? Alignment.centerRight : Alignment.centerLeft,
                                      child: _bubble(msg, isMe, sameAsPrev, mine),
                                    ),
                                  ],
                                ),
                              );
                            },
                          ),
                if (!_searching && (_newBelow > 0 || !_atBottom) && _messages.isNotEmpty)
                  Positioned(
                    right: 14,
                    bottom: 12,
                    child: GestureDetector(
                      onTap: () {
                        setState(() => _newBelow = 0);
                        _toBottom();
                      },
                      child: Container(
                        padding: EdgeInsets.symmetric(horizontal: _newBelow > 0 ? 14 : 12, vertical: 9),
                        decoration: BoxDecoration(
                          color: _newBelow > 0 ? MlColors.primary : MlColors.surface,
                          borderRadius: BorderRadius.circular(MlRadii.full),
                          border: Border.all(color: _newBelow > 0 ? MlColors.primary : MlColors.border),
                          boxShadow: const [BoxShadow(color: Color(0x33000000), blurRadius: 8, offset: Offset(0, 3))],
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            MlIcon(MlIcons.down, size: 20, color: _newBelow > 0 ? _white : MlColors.textPrimary),
                            if (_newBelow > 0)
                              Text(' $_newBelow new',
                                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: _white, decoration: TextDecoration.none)),
                          ],
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
          if (!_searching) _composer(),
        ],
      ),
    );
  }
}
