import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import 'button.dart';
import 'icons.dart';

/// Pull-to-refresh: a round indicator drops in from the top as you pull, fills as you approach the release point,
/// ticks once when it is ready, then spins while loading and slides away. No Material widgets.
class MlPullToRefresh extends StatefulWidget {
  const MlPullToRefresh({
    super.key,
    required this.child,
    required this.onRefresh,
    this.displacement = 64.0,
  });

  final Widget child;
  final Future<void> Function() onRefresh;
  final double displacement;

  @override
  State<MlPullToRefresh> createState() => _MlPullToRefreshState();
}

class _MlPullToRefreshState extends State<MlPullToRefresh> {
  double _drag = 0.0;
  bool _refreshing = false;
  bool _armed = false;

  bool _onScroll(ScrollNotification n) {
    if (_refreshing || n.depth != 0) return false;

    if (n is OverscrollNotification && n.overscroll < 0) {
      setState(() {
        _drag = (_drag - n.overscroll * 0.55).clamp(0.0, widget.displacement * 1.6);
        final ready = _drag >= widget.displacement;
        if (ready && !_armed) HapticFeedback.selectionClick();
        _armed = ready;
      });
    } else if (n is ScrollUpdateNotification && n.metrics.pixels > 0 && _drag > 0) {
      setState(() {
        _drag = 0;
        _armed = false;
      });
    } else if (n is ScrollEndNotification) {
      if (_armed) {
        _trigger();
      } else if (_drag > 0) {
        setState(() => _drag = 0);
      }
    }
    return false;
  }

  Future<void> _trigger() async {
    setState(() {
      _refreshing = true;
      _drag = widget.displacement;
    });
    try {
      await widget.onRefresh();
    } finally {
      if (mounted) {
        setState(() {
          _refreshing = false;
          _armed = false;
          _drag = 0;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final progress = (_drag / widget.displacement).clamp(0.0, 1.0);
    final show = _drag > 0 || _refreshing;
    return Stack(
      children: [
        NotificationListener<ScrollNotification>(onNotification: _onScroll, child: widget.child),
        Positioned(
          top: 0,
          left: 0,
          right: 0,
          height: widget.displacement,
          child: IgnorePointer(
            child: AnimatedOpacity(
              duration: const Duration(milliseconds: 160),
              opacity: show ? 1 : 0,
              child: Align(
                alignment: Alignment.topCenter,
                child: Transform.translate(
                  offset: Offset(0, _drag - 46),
                  child: Transform.scale(
                    scale: 0.6 + 0.4 * progress,
                    child: Container(
                      width: 42,
                      height: 42,
                      decoration: BoxDecoration(
                        color: MlColors.surface,
                        shape: BoxShape.circle,
                        border: Border.all(color: MlColors.border),
                        boxShadow: const [BoxShadow(color: Color(0x30000000), blurRadius: 10, offset: Offset(0, 3))],
                      ),
                      alignment: Alignment.center,
                      child: _refreshing
                          ? const MlSpinner(size: 22)
                          : Transform.rotate(
                              angle: progress * 6.28,
                              child: Icon(
                                MlIcons.refresh,
                                size: 22,
                                color: _armed ? MlColors.primary : MlColors.textMuted,
                              ),
                            ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
