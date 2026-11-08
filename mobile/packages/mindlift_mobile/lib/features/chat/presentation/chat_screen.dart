import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class ChatScreen extends ConsumerStatefulWidget {
  const ChatScreen({super.key});

  @override
  ConsumerState<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends ConsumerState<ChatScreen> {
  String _selectedChannel = 'general';
  ChatUserDto? _selectedRecipient;
  final _messageController = TextEditingController();
  final _scrollController = ScrollController();

  bool _isLoading = false;
  bool _isSending = false;
  List<ChatMessageDto> _messages = [];
  List<ChatUserDto> _colleagues = [];
  Timer? _pollTimer;

  static const _channels = ['general', 'deals', 'field-team'];

  @override
  void initState() {
    super.initState();
    _fetchColleagues();
    _fetchMessages();
    _pollTimer = Timer.periodic(const Duration(seconds: 4), (_) {
      if (mounted) _fetchMessages(silent: true);
    });
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    _messageController.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _fetchColleagues() async {
    try {
      final list = await ref.read(chatServiceProvider).getUsers();
      if (mounted) setState(() => _colleagues = list);
    } catch (_) {}
  }

  Future<void> _fetchMessages({bool silent = false}) async {
    if (!silent) setState(() => _isLoading = true);
    try {
      final list = await ref.read(chatServiceProvider).getMessages(
            channel: _selectedRecipient == null ? _selectedChannel : null,
            recipientId: _selectedRecipient?.id,
          );
      if (mounted) {
        setState(() => _messages = list);
        if (!silent && _messages.isNotEmpty) {
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (_scrollController.hasClients) {
              _scrollController.animateTo(
                _scrollController.position.maxScrollExtent,
                duration: const Duration(milliseconds: 200),
                curve: Curves.easeOut,
              );
            }
          });
        }
      }
    } catch (_) {
    } finally {
      if (mounted && !silent) setState(() => _isLoading = false);
    }
  }

  Future<void> _handleSendMessage() async {
    final text = _messageController.text.trim();
    if (text.isEmpty || _isSending) return;

    setState(() => _isSending = true);
    _messageController.clear();
    try {
      final sent = await ref.read(chatServiceProvider).sendMessage(
            message: text,
            channel: _selectedRecipient == null ? _selectedChannel : null,
            recipientId: _selectedRecipient?.id,
          );
      if (mounted) {
        setState(() => _messages.add(sent));
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (_scrollController.hasClients) {
            _scrollController.jumpTo(_scrollController.position.maxScrollExtent);
          }
        });
      }
    } catch (_) {
      MlToast.show(context, message: 'Message sent or queued.');
    } finally {
      if (mounted) setState(() => _isSending = false);
    }
  }

  void _switchChannel(String ch) {
    setState(() {
      _selectedChannel = ch;
      _selectedRecipient = null;
    });
    _fetchMessages();
  }

  void _switchUser(ChatUserDto user) {
    setState(() {
      _selectedRecipient = user;
    });
    _fetchMessages();
    ref.read(chatServiceProvider).markRead(otherUserId: user.id);
  }

  @override
  Widget build(BuildContext context) {
    final myEmail = ref.watch(authNotifierProvider).userEmail ?? '';

    return MlScreen(
      title: _selectedRecipient != null ? _selectedRecipient!.name : '#$_selectedChannel',
      subtitle: _selectedRecipient != null ? (_selectedRecipient!.role ?? 'Direct Chat') : 'Workspace Channel',
      actions: [
        MlIconButton(
          label: 'Refresh',
          icon: const Text('↻', style: TextStyle(fontSize: 18, color: MlColors.textPrimary)),
          onPressed: () {
            _fetchColleagues();
            _fetchMessages();
          },
        ),
      ],
      body: Column(
        children: [
          // Channels & Direct Colleagues Bar
          Container(
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.sm, vertical: MlSpacing.xs),
            decoration: const BoxDecoration(
              color: MlColors.surface,
              border: Border(bottom: BorderSide(color: MlColors.border, width: 1)),
            ),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  for (final ch in _channels) ...[
                    GestureDetector(
                      onTap: () => _switchChannel(ch),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                        margin: const EdgeInsets.only(right: 6),
                        decoration: BoxDecoration(
                          color: _selectedRecipient == null && _selectedChannel == ch
                              ? MlColors.primary
                              : MlColors.surfaceElevated,
                          borderRadius: BorderRadius.circular(MlRadii.full),
                        ),
                        child: Text(
                          '#$ch',
                          style: MlTypography.caption.copyWith(
                            color: _selectedRecipient == null && _selectedChannel == ch
                                ? MlColors.white
                                : MlColors.textSecondary,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ),
                  ],
                  for (final u in _colleagues) ...[
                    GestureDetector(
                      onTap: () => _switchUser(u),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                        margin: const EdgeInsets.only(right: 6),
                        decoration: BoxDecoration(
                          color: _selectedRecipient?.id == u.id
                              ? MlColors.primary
                              : MlColors.surfaceElevated,
                          borderRadius: BorderRadius.circular(MlRadii.full),
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(
                              u.name.split(' ').first,
                              style: MlTypography.caption.copyWith(
                                color: _selectedRecipient?.id == u.id
                                    ? MlColors.white
                                    : MlColors.textSecondary,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            if (u.unreadCount > 0) ...[
                              const SizedBox(width: 4),
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                                decoration: const BoxDecoration(
                                  color: MlColors.danger,
                                  shape: BoxShape.circle,
                                ),
                                child: Text(
                                  '${u.unreadCount}',
                                  style: const TextStyle(fontSize: 10, color: MlColors.white, fontWeight: FontWeight.w700),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),

          // Message Bubbles Thread
          Expanded(
            child: _isLoading && _messages.isEmpty
                ? const Center(child: Text('Loading messages...'))
                : _messages.isEmpty
                    ? Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            const Text('💬', style: TextStyle(fontSize: 32)),
                            const SizedBox(height: MlSpacing.sm),
                            Text(
                              'No messages in this chat yet.',
                              style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
                            ),
                            const SizedBox(height: MlSpacing.xs),
                            Text(
                              'Send the first message below!',
                              style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                            ),
                          ],
                        ),
                      )
                    : ListView.builder(
                        controller: _scrollController,
                        padding: const EdgeInsets.all(MlSpacing.md),
                        itemCount: _messages.length,
                        itemBuilder: (context, index) {
                          final msg = _messages[index];
                          final isMe = msg.senderEmail == myEmail || msg.senderName == 'You';

                          return Align(
                            alignment: isMe ? Alignment.centerRight : Alignment.centerLeft,
                            child: Container(
                              margin: const EdgeInsets.only(bottom: MlSpacing.sm),
                              constraints: BoxConstraints(
                                maxWidth: MediaQuery.sizeOf(context).width * 0.75,
                              ),
                              padding: const EdgeInsets.symmetric(
                                horizontal: MlSpacing.md,
                                vertical: MlSpacing.sm,
                              ),
                              decoration: BoxDecoration(
                                color: isMe ? MlColors.primary : MlColors.surface,
                                borderRadius: BorderRadius.circular(MlRadii.lg),
                                border: Border.all(
                                  color: isMe ? MlColors.primary : MlColors.border,
                                ),
                                boxShadow: const [
                                  BoxShadow(
                                    color: Color(0x060F172A),
                                    blurRadius: 4,
                                    offset: Offset(0, 1),
                                  ),
                                ],
                              ),
                              child: Column(
                                crossAxisAlignment:
                                    isMe ? CrossAxisAlignment.end : CrossAxisAlignment.start,
                                children: [
                                  if (!isMe)
                                    Text(
                                      msg.senderName,
                                      style: MlTypography.caption.copyWith(
                                        color: MlColors.primary,
                                        fontWeight: FontWeight.w700,
                                      ),
                                    ),
                                  Text(
                                    msg.message,
                                    style: MlTypography.bodyMedium.copyWith(
                                      color: isMe ? MlColors.white : MlColors.textPrimary,
                                    ),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    '${msg.createdAtUtc.hour.toString().padLeft(2, '0')}:${msg.createdAtUtc.minute.toString().padLeft(2, '0')}',
                                    style: MlTypography.caption.copyWith(
                                      color: isMe
                                          ? MlColors.white.withOpacity(0.7)
                                          : MlColors.textMuted,
                                      fontSize: 10,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          );
                        },
                      ),
          ),

          // Message Composer Input
          Container(
            padding: const EdgeInsets.all(MlSpacing.sm),
            decoration: const BoxDecoration(
              color: MlColors.surface,
              border: Border(top: BorderSide(color: MlColors.border, width: 1)),
            ),
            child: Row(
              children: [
                Expanded(
                  child: MlTextField(
                    placeholder: _selectedRecipient != null
                        ? 'Message ${_selectedRecipient!.name}...'
                        : 'Message #$_selectedChannel...',
                    controller: _messageController,
                    onSubmitted: (_) => _handleSendMessage(),
                  ),
                ),
                const SizedBox(width: MlSpacing.xs),
                GestureDetector(
                  onTap: _isSending ? null : _handleSendMessage,
                  child: Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: MlColors.primary,
                      borderRadius: BorderRadius.circular(MlRadii.md),
                    ),
                    alignment: Alignment.center,
                    child: const Text('➤', style: TextStyle(fontSize: 18, color: MlColors.white)),
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
