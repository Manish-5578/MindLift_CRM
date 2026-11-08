import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';

class MlNavigationDestination {
  const MlNavigationDestination({
    required this.label,
    required this.icon,
    this.activeIcon,
    this.badgeCount,
  });

  final String label;
  final Widget icon;
  final Widget? activeIcon;
  final int? badgeCount;
}

/// Custom 5-destination bottom navigation bar with a dedicated center Quick Add button.
/// Strictly built from Flutter base primitives without Material widgets.
class MlBottomNavigation extends StatelessWidget {
  MlBottomNavigation({
    super.key,
    required this.currentIndex,
    required this.onTap,
    required this.destinations,
    this.onQuickAdd,
    Color? backgroundColor,
    Color? borderColor,
  }) : _backgroundColor = backgroundColor, _borderColor = borderColor, assert(destinations.length <= 5, 'Maximum 5 destinations allowed.');

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
    // If quick add is provided and we have 4 or 5 destinations, we can insert the quick-add
    // in the center or provide it alongside.
    final hasCenterQuickAdd = onQuickAdd != null && destinations.length == 4;

    return Container(
      decoration: BoxDecoration(
        color: backgroundColor,
        border: Border(top: BorderSide(color: borderColor, width: 1.0)),
      ),
      child: SafeArea(
        top: false,
        child: Container(
          height: 64,
          padding: const EdgeInsets.symmetric(horizontal: MlSpacing.xs),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceAround,
            children: [
              if (!hasCenterQuickAdd)
                for (int i = 0; i < destinations.length; i++)
                  Expanded(
                    child: _buildItem(context, i, destinations[i]),
                  )
              else ...[
                Expanded(child: _buildItem(context, 0, destinations[0])),
                Expanded(child: _buildItem(context, 1, destinations[1])),
                _buildQuickAddButton(context),
                Expanded(child: _buildItem(context, 2, destinations[2])),
                Expanded(child: _buildItem(context, 3, destinations[3])),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildQuickAddButton(BuildContext context) {
    return Semantics(
      button: true,
      label: 'Quick Add Lead, Task, or Call',
      child: GestureDetector(
        onTap: onQuickAdd,
        behavior: HitTestBehavior.opaque,
        child: Container(
          width: 48,
          height: 48,
          margin: const EdgeInsets.symmetric(horizontal: 4),
          decoration: BoxDecoration(
            color: MlColors.primary,
            shape: BoxShape.circle,
            boxShadow: [
              BoxShadow(
                color: MlColors.primary.withOpacity(0.35),
                blurRadius: 8,
                offset: const Offset(0, 3),
              ),
            ],
          ),
          alignment: Alignment.center,
          child: const Text(
            '+',
            style: TextStyle(
              fontSize: 28,
              fontWeight: FontWeight.w400,
              color: MlColors.white,
              height: 1.0,
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildItem(BuildContext context, int index, MlNavigationDestination item) {
    final isSelected = index == currentIndex;

    return Semantics(
      button: true,
      selected: isSelected,
      label: item.label,
      child: GestureDetector(
        onTap: () => onTap(index),
        behavior: HitTestBehavior.opaque,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: MlSpacing.touchTarget),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Stack(
                clipBehavior: Clip.none,
                children: [
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                    decoration: BoxDecoration(
                      color: isSelected ? MlColors.primary.withOpacity(0.12) : const Color(0x00000000),
                      borderRadius: BorderRadius.circular(MlRadii.full),
                    ),
                    child: isSelected && item.activeIcon != null ? item.activeIcon! : item.icon,
                  ),
                  if (item.badgeCount != null && item.badgeCount! > 0)
                    Positioned(
                      top: -2,
                      right: 4,
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 2),
                        decoration: BoxDecoration(
                          color: MlColors.danger,
                          borderRadius: BorderRadius.circular(MlRadii.full),
                        ),
                        constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
                        alignment: Alignment.center,
                        child: Text(
                          item.badgeCount! > 99 ? '99+' : '${item.badgeCount}',
                          style: const TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.w700,
                            color: MlColors.white,
                            height: 1.0,
                          ),
                        ),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 2),
              Text(
                item.label,
                style: MlTypography.caption.copyWith(
                  fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
                  color: isSelected ? MlColors.primary : MlColors.textMuted,
                ),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

