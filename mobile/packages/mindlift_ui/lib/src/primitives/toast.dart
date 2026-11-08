import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import 'icons.dart';

enum MlToastType { success, error, info }

/// Transient notification toast replacing SnackBar.
class MlToast {
  const MlToast._();

  static void show(
    BuildContext context, {
    required String message,
    MlToastType type = MlToastType.info,
    Color? backgroundColor,
    Duration duration = const Duration(seconds: 3),
  }) {
    final overlay = Overlay.maybeOf(context);
    if (overlay == null) return;

    late OverlayEntry entry;
    entry = OverlayEntry(
      builder: (ctx) => _ToastWidget(
        message: message,
        type: type,
        customBackgroundColor: backgroundColor,
        onDismiss: () => entry.remove(),
      ),
    );

    overlay.insert(entry);
    Future.delayed(duration, () {
      if (entry.mounted) entry.remove();
    });
  }
}

class _ToastWidget extends StatefulWidget {
  const _ToastWidget({
    required this.message,
    required this.type,
    this.customBackgroundColor,
    required this.onDismiss,
  });

  final String message;
  final MlToastType type;
  final Color? customBackgroundColor;
  final VoidCallback onDismiss;

  @override
  State<_ToastWidget> createState() => _ToastWidgetState();
}

class _ToastWidgetState extends State<_ToastWidget> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 260))..forward();

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Always a dark surface with light text, so the message is readable in light and dark mode.
    final bg = widget.customBackgroundColor ?? const Color(0xFF1F2937);
    final (icon, accent) = switch (widget.type) {
      MlToastType.success => (MlIcons.checkCircle, const Color(0xFF34D399)),
      MlToastType.error => (MlIcons.warning, const Color(0xFFF87171)),
      MlToastType.info => (MlIcons.sparkle, const Color(0xFFA5B4FC)),
    };
    final curved = CurvedAnimation(parent: _c, curve: Curves.easeOutCubic);

    return Positioned(
      bottom: MediaQuery.paddingOf(context).bottom + 92,
      left: 16,
      right: 16,
      child: FadeTransition(
        opacity: curved,
        child: SlideTransition(
          position: Tween<Offset>(begin: const Offset(0, 0.4), end: Offset.zero).animate(curved),
          child: GestureDetector(
            onTap: widget.onDismiss,
            child: Center(
              child: Container(
                constraints: const BoxConstraints(maxWidth: 520),
                padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
                decoration: BoxDecoration(
                  color: bg,
                  borderRadius: BorderRadius.circular(MlRadii.lg),
                  boxShadow: const [BoxShadow(color: Color(0x55000000), blurRadius: 16, offset: Offset(0, 6))],
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(icon, size: 22, color: accent),
                    const SizedBox(width: 10),
                    Flexible(
                      child: Text(
                        widget.message,
                        style: const TextStyle(fontSize: 14, height: 1.3, fontWeight: FontWeight.w500, color: MlColors.white, decoration: TextDecoration.none),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
