import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';

/// Cut / Copy / Paste / Select all pill shown on long-press in text fields.
/// (The default toolbar needs Material localizations, which this app does not use, and draws an empty pill.)
class MlSelectionToolbar extends StatelessWidget {
  const MlSelectionToolbar({super.key, required this.state});
  final EditableTextState state;

  static String _label(ContextMenuButtonItem item) {
    if (item.label != null && item.label!.isNotEmpty) return item.label!;
    return switch (item.type) {
      ContextMenuButtonType.cut => 'Cut',
      ContextMenuButtonType.copy => 'Copy',
      ContextMenuButtonType.paste => 'Paste',
      ContextMenuButtonType.selectAll => 'Select all',
      ContextMenuButtonType.delete => 'Delete',
      _ => 'Action',
    };
  }

  @override
  Widget build(BuildContext context) {
    final anchors = state.contextMenuAnchors;
    final items = state.contextMenuButtonItems;
    if (items.isEmpty) return const SizedBox.shrink();
    final size = MediaQuery.sizeOf(context);
    final dx = anchors.primaryAnchor.dx.clamp(100.0, size.width - 100.0);
    var top = anchors.primaryAnchor.dy - 56;
    if (top < MediaQuery.paddingOf(context).top + 8) top = (anchors.secondaryAnchor ?? anchors.primaryAnchor).dy + 12;

    return Stack(
      children: [
        Positioned(
          left: dx - 100,
          top: top,
          width: 200,
          child: Align(
            alignment: Alignment.topCenter,
            child: TextFieldTapRegion(
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 4),
                decoration: BoxDecoration(
                  color: const Color(0xFF1F2937),
                  borderRadius: BorderRadius.circular(12),
                  boxShadow: const [BoxShadow(color: Color(0x40000000), blurRadius: 12, offset: Offset(0, 4))],
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    for (final item in items)
                      GestureDetector(
                        behavior: HitTestBehavior.opaque,
                        onTap: item.onPressed,
                        child: Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
                          child: Text(
                            _label(item),
                            style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: MlColors.white, decoration: TextDecoration.none),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
