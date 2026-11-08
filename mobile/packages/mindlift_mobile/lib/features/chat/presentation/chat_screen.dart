import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

/// Direct, person-to-person messaging between colleagues of the same company (no public channels).
/// Shows a conversation list first; tapping a person opens a WhatsApp-style thread.
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
      if (mounted && _open == null) _loadPeople(silent: true);
    });
  }

  @override
  void dispose() {
    _listTimer?.cancel();
    super.dispose();
  }

  Future<void> _loadPeople({bool silent = false}) async {
    try {
      final list = await ref.read(chatServiceProvider).getUsers();
      list.sort((a, b) {
        if (a.unreadCount != b.unreadCount) return b.unreadCount.compareTo(a.unreadCount);
        return a.name.toLowerCase().compareTo(b.name.toLowerCase());
      });
      if (mounted) setState(() => _people = list);
    } catch (_) {
    } finally {
      if (mounted && _loadingPeople) setState(() => _loadingPeople = false);
    }
  }

  void _close() {
    setState(() => _open = null);
    _loadPeople(silent: true);
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

    return MlScreen(
      title: 'Chats',
      subtitle: '${_people.length} colleagues',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(MlSpacing.md, MlSpacing.sm, MlSpacing.md, MlSpacing.xs),
            child: MlTextField(placeholder: 'Search people', onChanged: (v) => setState(() => _query = v)),
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
                        itemCount: shown.length,
                        itemExtent: 72,
                        itemBuilder: (context, i) {
                          final p = shown[i];
                          return _PersonTile(person: p, onTap: () => setState(() => _open = p));
                        },
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
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md),
        decoration: BoxDecoration(border: Border(bottom: BorderSide(color: MlColors.divider, width: 0.5))),
        child: Row(
          children: [
            Stack(
              children: [
                MlAvatar(name: person.name, size: MlAvatarSize.lg),
                if (person.isOnline)
                  Positioned(
                    right: 0,
                    bottom: 0,
                    child: Container(
                      width: 12,
                      height: 12,
                      decoration: BoxDecoration(
                        color: MlColors.success,
                        shape: BoxShape.circle,
                        border: Border.all(color: MlColors.background, width: 2),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(width: MlSpacing.md),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(person.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: MlTypography.labelMedium.copyWith(
                          color: MlColors.textPrimary,
                          fontWeight: person.unreadCount > 0 ? FontWeight.w700 : FontWeight.w600)),
                  const SizedBox(height: 2),
                  Text(person.role ?? 'Team member',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                ],
              ),
            ),
            if (person.unreadCount > 0)
              Container(
                constraints: const BoxConstraints(minWidth: 22),
                padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
                decoration: BoxDecoration(color: MlColors.primary, borderRadius: BorderRadius.circular(MlRadii.full)),
                child: Text('${person.unreadCount}',
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 11, color: Color(0xFFFFFFFF), fontWeight: FontWeight.w700)),
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
  bool _sending = false;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _fetch(initial: true);
    _poll = Timer.periodic(const Duration(seconds: 4), (_) {
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
        _scroll.animateTo(max, duration: const Duration(milliseconds: 180), curve: Curves.easeOut);
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
      final changed = list.length != _messages.length || (list.isNotEmpty && _messages.isNotEmpty && list.last.id != _messages.last.id);
      if (changed || initial) {
        final atBottom = !_scroll.hasClients || _scroll.position.maxScrollExtent - _scroll.offset < 80;
        setState(() => _messages = list);
        if (initial) {
          _toBottom(animate: false);
        } else if (atBottom) {
          _toBottom();
        }
        svc.markRead(otherUserId: widget.person.id).catchError((_) {});
      }
    } catch (_) {
    } finally {
      if (mounted && _loading) setState(() => _loading = false);
    }
  }

  Future<void> _send() async {
    final text = _controller.text.trim();
    if (text.isEmpty || _sending) return;
    setState(() => _sending = true);
    _controller.clear();
    try {
      final sent = await ref.read(chatServiceProvider).sendMessage(message: text, recipientId: widget.person.id);
      if (mounted) {
        setState(() => _messages = [..._messages, sent]);
        _toBottom();
      }
    } catch (_) {
      if (mounted) {
        _controller.text = text;
        MlToast.show(context, message: 'Could not send. Check your connection.');
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  static String _hm(DateTime utc) {
    final t = utc.toLocal();
    return '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
  }

  static String _day(DateTime utc) {
    final t = utc.toLocal();
    final now = DateTime.now();
    final d = DateTime(t.year, t.month, t.day);
    final today = DateTime(now.year, now.month, now.day);
    final diff = today.difference(d).inDays;
    if (diff == 0) return 'Today';
    if (diff == 1) return 'Yesterday';
    const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return '${t.day} ${m[t.month - 1]} ${t.year}';
  }

  @override
  Widget build(BuildContext context) {
    final p = widget.person;
    final myEmail = ref.watch(authNotifierProvider).userEmail ?? '';
    final bubbleMine = MlColors.isDark ? const Color(0xFF3730A3) : MlColors.primary;
    final wallpaper = MlColors.isDark ? const Color(0xFF0B1120) : const Color(0xFFEFEAF7);

    return MlScreen(
      backgroundColor: wallpaper,
      leading: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onBack,
        child: Padding(
          padding: const EdgeInsets.only(right: MlSpacing.sm),
          child: Text('‹', style: TextStyle(fontSize: 34, height: 1, color: MlColors.textPrimary, fontWeight: FontWeight.w300)),
        ),
      ),
      title: p.name,
      subtitle: p.isOnline ? 'Online' : (p.role ?? 'Offline'),
      body: Column(
        children: [
          Expanded(
            child: _loading && _messages.isEmpty
                ? const Center(child: Text('Loading...'))
                : _messages.isEmpty
                    ? Center(
                        child: Container(
                          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
                          decoration: BoxDecoration(color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.lg)),
                          child: Text('Say hi to ${p.name.split(' ').first} 👋',
                              style: MlTypography.bodyMedium.copyWith(color: MlColors.textSecondary)),
                        ),
                      )
                    : ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.symmetric(horizontal: MlSpacing.sm, vertical: MlSpacing.sm),
                        itemCount: _messages.length,
                        itemBuilder: (context, i) {
                          final msg = _messages[i];
                          final isMe = msg.senderEmail == myEmail || msg.senderName == 'You';
                          final showDay = i == 0 || _day(_messages[i - 1].createdAtUtc) != _day(msg.createdAtUtc);
                          final sameAsPrev = i > 0 && !showDay && (_messages[i - 1].senderId == msg.senderId);
                          return RepaintBoundary(
                            child: Column(
                              children: [
                                if (showDay)
                                  Container(
                                    margin: const EdgeInsets.symmetric(vertical: MlSpacing.sm),
                                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                                    decoration: BoxDecoration(
                                        color: MlColors.surface, borderRadius: BorderRadius.circular(MlRadii.full)),
                                    child: Text(_day(msg.createdAtUtc),
                                        style: MlTypography.caption.copyWith(color: MlColors.textMuted, fontSize: 11)),
                                  ),
                                Align(
                                  alignment: isMe ? Alignment.centerRight : Alignment.centerLeft,
                                  child: Container(
                                    margin: EdgeInsets.only(bottom: 3, top: sameAsPrev ? 0 : 5),
                                    constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.78),
                                    padding: const EdgeInsets.fromLTRB(11, 7, 9, 5),
                                    decoration: BoxDecoration(
                                      color: isMe ? bubbleMine : MlColors.surface,
                                      borderRadius: BorderRadius.only(
                                        topLeft: const Radius.circular(14),
                                        topRight: const Radius.circular(14),
                                        bottomLeft: Radius.circular(isMe ? 14 : 3),
                                        bottomRight: Radius.circular(isMe ? 3 : 14),
                                      ),
                                    ),
                                    child: Column(
                                      crossAxisAlignment: CrossAxisAlignment.end,
                                      mainAxisSize: MainAxisSize.min,
                                      children: [
                                        Align(
                                          alignment: Alignment.centerLeft,
                                          child: Text(msg.message,
                                              style: MlTypography.bodyMedium.copyWith(
                                                  color: isMe ? const Color(0xFFFFFFFF) : MlColors.textPrimary)),
                                        ),
                                        const SizedBox(height: 2),
                                        Row(
                                          mainAxisSize: MainAxisSize.min,
                                          children: [
                                            Text(_hm(msg.createdAtUtc),
                                                style: TextStyle(
                                                    fontSize: 10,
                                                    color: isMe ? const Color(0xB3FFFFFF) : MlColors.textMuted)),
                                            if (isMe) ...[
                                              const SizedBox(width: 3),
                                              Text(msg.isRead ? '✓✓' : '✓',
                                                  style: TextStyle(
                                                      fontSize: 10,
                                                      fontWeight: FontWeight.w700,
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
          Container(
            padding: const EdgeInsets.fromLTRB(MlSpacing.sm, MlSpacing.xs, MlSpacing.sm, MlSpacing.sm),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: Container(
                    decoration: BoxDecoration(
                      color: MlColors.surface,
                      borderRadius: BorderRadius.circular(24),
                      border: Border.all(color: MlColors.border),
                    ),
                    padding: const EdgeInsets.symmetric(horizontal: 6),
                    child: MlTextField(
                      placeholder: 'Message',
                      controller: _controller,
                      maxLines: 5,
                      minLines: 1,
                      onSubmitted: (_) => _send(),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                GestureDetector(
                  onTap: _send,
                  child: Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(color: MlColors.primary, shape: BoxShape.circle),
                    alignment: Alignment.center,
                    child: const Text('➤', style: TextStyle(fontSize: 19, color: Color(0xFFFFFFFF))),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
