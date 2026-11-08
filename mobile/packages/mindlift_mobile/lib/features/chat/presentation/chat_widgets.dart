import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';
import '../../../core/network_provider.dart';

const _white = Color(0xFFFFFFFF);

/// Downloaded attachments are kept for the session so scrolling back never fetches twice.
final Map<String, Uint8List> _bytesCache = {};

Future<Uint8List> _bytesFor(WidgetRef ref, ChatMessageDto m) async {
  final cached = _bytesCache[m.id];
  if (cached != null) return cached;
  final data = Uint8List.fromList(await ref.read(chatServiceProvider).downloadAttachment(m.id));
  _bytesCache[m.id] = data;
  return data;
}

Future<File> _tempFile(WidgetRef ref, ChatMessageDto m) async {
  final dir = await getTemporaryDirectory();
  final safe = (m.attachmentName ?? 'file').replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
  final file = File('${dir.path}/chat_${m.id}_$safe');
  if (!await file.exists()) await file.writeAsBytes(await _bytesFor(ref, m));
  return file;
}

String sizeLabel(int? bytes) {
  if (bytes == null) return '';
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}

String clock(int ms) {
  final s = (ms / 1000).round();
  return '${(s ~/ 60)}:${(s % 60).toString().padLeft(2, '0')}';
}

/// The photo, voice note or document inside a message bubble.
class AttachmentView extends ConsumerStatefulWidget {
  const AttachmentView({super.key, required this.message, required this.isMe});
  final ChatMessageDto message;
  final bool isMe;

  @override
  ConsumerState<AttachmentView> createState() => _AttachmentViewState();
}

class _AttachmentViewState extends ConsumerState<AttachmentView> {
  Uint8List? _image;
  bool _failed = false;
  AudioPlayer? _player;
  bool _playing = false;
  Duration _pos = Duration.zero;
  bool _busy = false;
  StreamSubscription<void>? _doneSub;
  StreamSubscription<Duration>? _posSub;

  Color get _fg => widget.isMe ? _white : MlColors.textPrimary;

  @override
  void initState() {
    super.initState();
    if (widget.message.attachmentKind == 'image') _loadImage();
  }

  @override
  void dispose() {
    _doneSub?.cancel();
    _posSub?.cancel();
    _player?.dispose();
    super.dispose();
  }

  Future<void> _loadImage() async {
    try {
      final data = await _bytesFor(ref, widget.message);
      if (mounted) setState(() => _image = data);
    } catch (_) {
      if (mounted) setState(() => _failed = true);
    }
  }

  Future<void> _togglePlay() async {
    try {
      if (_player == null) {
        setState(() => _busy = true);
        final file = await _tempFile(ref, widget.message);
        final p = AudioPlayer();
        _player = p;
        _posSub = p.onPositionChanged.listen((d) {
          if (mounted) setState(() => _pos = d);
        });
        _doneSub = p.onPlayerComplete.listen((_) {
          if (mounted) setState(() {
            _playing = false;
            _pos = Duration.zero;
          });
        });
        await p.play(DeviceFileSource(file.path));
        if (mounted) setState(() {
          _playing = true;
          _busy = false;
        });
        return;
      }
      if (_playing) {
        await _player!.pause();
      } else {
        await _player!.resume();
      }
      if (mounted) setState(() => _playing = !_playing);
    } catch (_) {
      if (mounted) {
        setState(() => _busy = false);
        MlToast.show(context, message: 'Could not play this voice message.');
      }
    }
  }

  Future<void> _openFile() async {
    setState(() => _busy = true);
    try {
      final file = await _tempFile(ref, widget.message);
      final res = await OpenFilex.open(file.path);
      if (res.type != ResultType.done && mounted) {
        MlToast.show(context, message: 'No app found to open this file.');
      }
    } catch (_) {
      if (mounted) MlToast.show(context, message: 'Could not open the file.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _viewImage() {
    final data = _image;
    if (data == null) return;
    Navigator.of(context).push(PageRouteBuilder<void>(
      opaque: true,
      pageBuilder: (c, _, __) => GestureDetector(
        onTap: () => Navigator.of(c).pop(),
        child: Container(
          color: const Color(0xFF000000),
          alignment: Alignment.center,
          child: InteractiveViewer(child: Image.memory(data, fit: BoxFit.contain)),
        ),
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.message;
    switch (m.attachmentKind) {
      case 'image':
        return GestureDetector(
          onTap: _viewImage,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: Container(
              width: 220,
              height: 220,
              color: widget.isMe ? const Color(0x33FFFFFF) : MlColors.surfaceMuted,
              alignment: Alignment.center,
              child: _image != null
                  ? Image.memory(_image!, width: 220, height: 220, fit: BoxFit.cover, gaplessPlayback: true)
                  : Text(_failed ? 'Photo unavailable' : 'Loading photo…',
                      style: TextStyle(fontSize: 12, color: _fg, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
            ),
          ),
        );
      case 'audio':
        final total = m.attachmentDurationMs ?? 0;
        final progress = total == 0 ? 0.0 : (_pos.inMilliseconds / total).clamp(0.0, 1.0);
        return SizedBox(
          width: 210,
          child: Row(
            children: [
              GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: _busy ? null : _togglePlay,
                child: Container(
                  width: 38,
                  height: 38,
                  decoration: BoxDecoration(color: widget.isMe ? const Color(0x33FFFFFF) : MlColors.primary, shape: BoxShape.circle),
                  alignment: Alignment.center,
                  child: Text(_busy ? '…' : (_playing ? '❚❚' : '▶'),
                      style: const TextStyle(fontSize: 14, color: _white, decoration: TextDecoration.none)),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Container(
                      height: 4,
                      decoration: BoxDecoration(color: _fg.withOpacity(0.25), borderRadius: BorderRadius.circular(2)),
                      alignment: Alignment.centerLeft,
                      child: FractionallySizedBox(
                        widthFactor: progress,
                        child: Container(decoration: BoxDecoration(color: _fg, borderRadius: BorderRadius.circular(2))),
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(clock(_playing || _pos > Duration.zero ? _pos.inMilliseconds : total),
                        style: TextStyle(fontSize: 11, color: _fg.withOpacity(0.8), decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                  ],
                ),
              ),
            ],
          ),
        );
      default:
        return GestureDetector(
          onTap: _busy ? null : _openFile,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 38,
                height: 38,
                decoration: BoxDecoration(color: widget.isMe ? const Color(0x33FFFFFF) : MlColors.surfaceMuted, borderRadius: BorderRadius.circular(10)),
                alignment: Alignment.center,
                child: Text(_busy ? '…' : '📄', style: const TextStyle(fontSize: 18, decoration: TextDecoration.none)),
              ),
              const SizedBox(width: 10),
              Flexible(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(m.attachmentName ?? 'File',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 14, color: _fg, fontWeight: FontWeight.w600, decoration: TextDecoration.none)),
                    Text(sizeLabel(m.attachmentSize),
                        style: TextStyle(fontSize: 11, color: _fg.withOpacity(0.75), decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
                  ],
                ),
              ),
            ],
          ),
        );
    }
  }
}

/// The quoted message shown above a reply.
class ReplyQuote extends StatelessWidget {
  const ReplyQuote({super.key, required this.sender, required this.text, required this.isMe, this.onTap});
  final String sender;
  final String text;
  final bool isMe;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final accent = isMe ? const Color(0xFFBFDBFE) : MlColors.primary;
    return GestureDetector(
      onTap: onTap,
      child: Container(
        margin: const EdgeInsets.only(bottom: 6),
        padding: const EdgeInsets.fromLTRB(8, 5, 8, 5),
        decoration: BoxDecoration(
          color: isMe ? const Color(0x26FFFFFF) : MlColors.surfaceMuted,
          borderRadius: BorderRadius.circular(8),
          border: Border(left: BorderSide(color: accent, width: 3)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(sender, style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w700, color: accent, decoration: TextDecoration.none)),
            Text(text,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12.5, color: isMe ? const Color(0xE6FFFFFF) : MlColors.textSecondary, decoration: TextDecoration.none, fontWeight: FontWeight.w400)),
          ],
        ),
      ),
    );
  }
}

/// Emoji chips under a bubble. Tapping one toggles your own reaction.
class ReactionsRow extends StatelessWidget {
  const ReactionsRow({super.key, required this.reactions, required this.onTap});
  final List<ChatReaction> reactions;
  final void Function(String emoji) onTap;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 4,
      runSpacing: 4,
      children: [
        for (final r in reactions)
          GestureDetector(
            onTap: () => onTap(r.emoji),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
              decoration: BoxDecoration(
                color: r.mine ? MlColors.primary.withOpacity(0.18) : MlColors.surface,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: r.mine ? MlColors.primary : MlColors.border),
              ),
              child: Text(r.count > 1 ? '${r.emoji} ${r.count}' : r.emoji,
                  style: TextStyle(fontSize: 12.5, color: MlColors.textPrimary, decoration: TextDecoration.none, fontWeight: FontWeight.w500)),
            ),
          ),
      ],
    );
  }
}
