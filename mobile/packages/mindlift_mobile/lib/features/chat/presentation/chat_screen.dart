import 'dart:async';
import 'package:flutter/services.dart' show TextInputAction;
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

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
                        placeholder: '🔍  Search people',
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
  final _scroll = ScrollController();
  List<ChatMessageDto> _messages = [];
  bool _loading = true;
  bool _hasText = false;
  late bool _online = widget.person.isOnline;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _controller.addListener(() {
      final has = _controller.text.trim().isNotEmpty;
      if (has != _hasText) setState(() => _hasText = has);
    });
    _fetch(initial: true);
    _poll = Timer.periodic(const Duration(seconds: 3), (_) {
      if (mounted) _fetch();
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    _controller.dispose();
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

  Future<void> _fetch({bool initial = false}) async {
    try {
      final svc = ref.read(chatServiceProvider);
      final list = await svc.getMessages(recipientId: widget.person.id);
      if (!mounted) return;
      final changed = list.length != _messages.length ||
          (list.isNotEmpty && _messages.isNotEmpty && (list.last.id != _messages.last.id || list.last.isRead != _messages.last.isRead));
      if (changed || initial) {
        final atBottom = !_scroll.hasClients || _scroll.position.maxScrollExtent - _scroll.offset < 100;
        setState(() => _messages = list);
        if (initial) {
          _toBottom(animate: false);
        } else if (atBottom) {
          _toBottom();
        }
        svc.markRead(otherUserId: widget.person.id).catchError((_) {});
      }
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
    _controller.clear();
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
    );
    setState(() => _messages = [..._messages, temp]);
    _toBottom();
    try {
      final sent = await ref.read(chatServiceProvider).sendMessage(message: text, recipientId: widget.person.id);
      if (mounted) setState(() => _messages = [..._messages.where((m) => m.id != temp.id), sent]);
    } catch (_) {
      if (mounted) {
        setState(() => _messages = _messages.where((m) => m.id != temp.id).toList());
        _controller.text = text;
        MlToast.show(context, message: 'Could not send. Check your connection.');
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = widget.person;
    final myEmail = ref.watch(authNotifierProvider).userEmail ?? '';
    final dark = MlColors.isDark;
    final mine = dark ? const Color(0xFF4338CA) : MlColors.primary;
    final wallpaper = dark ? const Color(0xFF0B1120) : const Color(0xFFEEF0FA);

    return Container(
      color: wallpaper,
      child: Column(
        children: [
          // Header
          Container(
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
                padding: const EdgeInsets.fromLTRB(4, 8, MlSpacing.md, 10),
                child: Row(
                  children: [
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: widget.onBack,
                      child: const Padding(
                        padding: EdgeInsets.symmetric(horizontal: 12, vertical: 2),
                        child: Text('‹',
                            style: TextStyle(fontSize: 38, height: 1, color: _white, fontWeight: FontWeight.w300, decoration: TextDecoration.none)),
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
                                child: Text(_online ? 'Online' : (p.role ?? 'Offline'),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(fontSize: 12, color: Color(0xCCFFFFFF), decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),

          // Messages
          Expanded(
            child: _loading && _messages.isEmpty
                ? const Center(child: Text('Loading...'))
                : _messages.isEmpty
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
                              const Text('👋', style: TextStyle(fontSize: 34, decoration: TextDecoration.none)),
                              const SizedBox(height: 6),
                              Text('Say hi to ${p.name.split(' ').first}',
                                  style: MlTypography.bodyMedium.copyWith(color: MlColors.textSecondary, fontWeight: FontWeight.w600)),
                            ],
                          ),
                        ),
                      )
                    : ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
                        itemCount: _messages.length,
                        itemBuilder: (context, i) {
                          final msg = _messages[i];
                          final isMe = msg.senderEmail == myEmail || msg.senderName == 'You';
                          final showDay = i == 0 || _day(_messages[i - 1].createdAtUtc) != _day(msg.createdAtUtc);
                          final sameAsPrev = i > 0 && !showDay && _messages[i - 1].senderId == msg.senderId;
                          final pending = msg.id.startsWith('local-');
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
                                              Text(pending ? '🕓' : (msg.isRead ? '✓✓' : '✓'),
                                                  style: TextStyle(
                                                      fontSize: pending ? 9 : 11,
                                                      fontWeight: FontWeight.w800,
                                                      decoration: TextDecoration.none,
                                                      color: msg.isRead ? const Color(0xFF7DD3FC) : const Color(0xB3FFFFFF))),
                                            ],
                                          ],
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          );
                        },
                      ),
          ),

          // Composer
          Container(
            padding: const EdgeInsets.fromLTRB(10, 8, 10, 10),
            decoration: BoxDecoration(
              color: MlColors.surface,
              border: Border(top: BorderSide(color: MlColors.border)),
            ),
            child: SafeArea(
              top: false,
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: Container(
                      decoration: BoxDecoration(
                        color: MlColors.surfaceElevated,
                        borderRadius: BorderRadius.circular(26),
                      ),
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
                    onTap: _send,
                    child: AnimatedContainer(
                      duration: const Duration(milliseconds: 150),
                      width: 50,
                      height: 50,
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          colors: _hasText ? [MlColors.primary, const Color(0xFF3730A3)] : [MlColors.textDisabled, MlColors.textDisabled],
                        ),
                        shape: BoxShape.circle,
                        boxShadow: _hasText ? const [BoxShadow(color: Color(0x554F46E5), blurRadius: 10, offset: Offset(0, 4))] : null,
                      ),
                      alignment: Alignment.center,
                      child: const Text('➤', style: TextStyle(fontSize: 20, color: _white, decoration: TextDecoration.none)),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
