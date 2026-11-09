import 'package:flutter/widgets.dart';
import '../tokens/strings.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/typography.dart';
import 'icons.dart';
import 'pressable.dart';

class MlNavigationDestination {
  const MlNavigationDestination({
    required this.label,
    required this.icon,
    this.activeIcon,
    this.badgeCount,
  });

  final String label;
  final IconData icon;
  final IconData? activeIcon;
  final int? badgeCount;
}

/// Material 3 style navigation bar: icon inside an animated pill, label below, and a raised centre
/// button for Quick Add when there are four destinations.
class MlBottomNavigation extends StatelessWidget {
  MlBottomNavigation({
    super.key,
    required this.currentIndex,
    required this.onTap,
    required this.destinations,
    this.onQuickAdd,
    Color? backgroundColor,
    Color? borderColor,
  })  : _backgroundColor = backgroundColor,
        _borderColor = borderColor,
        assert(destinations.length <= 5, 'Maximum 5 destinations allowed.');

  final int currentIndex;
  final ValueChanged<int> onTap;
  final List<MlNavigationDestination> destinations;
  final VoidCallback? onQuickAdd;
  final Color? _backgroundColor;
  Color get backgroundColor => _backgroundColor ?? MlColors.surface;
  final Color? _borderColor;
  Color get borderColor => _borderColor ?? MlColors.border;

  @override
  Widget build(BuildContext context) {
    final hasCenterQuickAdd = onQuickAdd != null && destinations.length == 4;

    return Container(
      decoration: BoxDecoration(
        color: backgroundColor,
        border: Border(top: BorderSide(color: borderColor, width: 1.0)),
        boxShadow: const [BoxShadow(color: Color(0x0F000000), blurRadius: 12, offset: Offset(0, -2))],
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 68,
          child: Row(
            children: [
              if (!hasCenterQuickAdd)
                for (var i = 0; i < destinations.length; i++) Expanded(child: _item(i, destinations[i]))
              else ...[
                Expanded(child: _item(0, destinations[0])),
                Expanded(child: _item(1, destinations[1])),
                _quickAdd(),
                Expanded(child: _item(2, destinations[2])),
                Expanded(child: _item(3, destinations[3])),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _quickAdd() {
    return Semantics(
      button: true,
      label: 'Quick add',
      child: MlPressable(
        onTap: onQuickAdd,
        scale: 0.9,
        child: Container(
          width: 52,
          height: 52,
          margin: const EdgeInsets.symmetric(horizontal: 6),
          decoration: BoxDecoration(
            gradient: const LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: [MlColors.primary, Color(0xFF3730A3)]),
            borderRadius: BorderRadius.circular(18),
            boxShadow: [BoxShadow(color: MlColors.primary.withOpacity(0.38), blurRadius: 12, offset: const Offset(0, 5))],
          ),
          alignment: Alignment.center,
          child: const Icon(MlIcons.add, size: 30, color: MlColors.white),
        ),
      ),
    );
  }

  Widget _item(int index, MlNavigationDestination item) {
    final selected = index == currentIndex;
    final color = selected ? MlColors.primary : MlColors.textMuted;

    return Semantics(
      button: true,
      selected: selected,
      label: item.label,
      child: MlPressable(
        onTap: () => onTap(index),
        scale: 0.92,
        haptic: !selected,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Stack(
              clipBehavior: Clip.none,
              children: [
                AnimatedContainer(
                  duration: const Duration(milliseconds: 280),
                  curve: Curves.easeOutCubic,
                  width: selected ? 60 : 36,
                  height: 32,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: selected ? MlColors.primary.withOpacity(0.14) : const Color(0x00000000),
                    borderRadius: BorderRadius.circular(MlRadii.full),
                  ),
                  child: AnimatedSwitcher(
                    duration: const Duration(milliseconds: 180),
                    child: Icon(
                      selected ? (item.activeIcon ?? item.icon) : item.icon,
                      key: ValueKey(selected),
                      size: 24,
                      color: color,
                    ),
                  ),
                ),
                if (item.badgeCount != null && item.badgeCount! > 0)
                  Positioned(
                    top: -3,
                    right: selected ? 8 : -2,
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                      decoration: BoxDecoration(color: MlColors.danger, borderRadius: BorderRadius.circular(MlRadii.full)),
                      constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
                      alignment: Alignment.center,
                      child: Text(
                        item.badgeCount! > 99 ? '99+' : '${item.badgeCount}',
                        style: const TextStyle(fontSize: 10, fontWeight: FontWeight.w700, color: MlColors.white, height: 1.0, decoration: TextDecoration.none),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 3),
            Text(
              MlStrings.t(item.label),
              style: MlTypography.caption.copyWith(fontWeight: selected ? FontWeight.w700 : FontWeight.w500, color: selected ? MlColors.primaryText : MlColors.textMuted),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ],
        ),
      ),
    );
  }
}
