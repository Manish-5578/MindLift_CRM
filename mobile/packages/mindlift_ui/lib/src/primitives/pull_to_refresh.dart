import 'dart:math' as math;
import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';

/// Zero-native pull-to-refresh container.
/// Uses Flutter scroll notification primitives instead of Material RefreshIndicator.
class MlPullToRefresh extends StatefulWidget {
  const MlPullToRefresh({
    super.key,
    required this.child,
    required this.onRefresh,
    this.displacement = 56.0,
  });

  final Widget child;
  final Future<void> Function() onRefresh;
  final double displacement;

  @override
  State<MlPullToRefresh> createState() => _MlPullToRefreshState();
}

class _MlPullToRefreshState extends State<MlPullToRefresh> with SingleTickerProviderStateMixin {
  double _dragOffset = 0.0;
  bool _isRefreshing = false;
  late AnimationController _spinController;

  @override
  void initState() {
    super.initState();
    _spinController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 900),
    );
  }

  @override
  void dispose() {
    _spinController.dispose();
    super.dispose();
  }

  bool _handleScrollNotification(ScrollNotification notification) {
    if (_isRefreshing) return false;

    if (notification is OverscrollNotification) {
      if (notification.overscroll < 0) {
        setState(() {
          _dragOffset -= notification.overscroll * 0.5;
          if (_dragOffset > widget.displacement * 1.5) {
            _dragOffset = widget.displacement * 1.5;
          }
        });
      }
    } else if (notification is ScrollEndNotification) {
      if (_dragOffset >= widget.displacement && !_isRefreshing) {
        _triggerRefresh();
      } else {
        setState(() => _dragOffset = 0.0);
      }
    }
    return false;
  }

  Future<void> _triggerRefresh() async {
    setState(() {
      _isRefreshing = true;
      _dragOffset = widget.displacement;
    });
    _spinController.repeat();

    try {
      await widget.onRefresh();
    } finally {
      if (mounted) {
        _spinController.stop();
        _spinController.reset();
        setState(() {
          _isRefreshing = false;
          _dragOffset = 0.0;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        NotificationListener<ScrollNotification>(
          onNotification: _handleScrollNotification,
          child: widget.child,
        ),
        if (_dragOffset > 0 || _isRefreshing)
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: Container(
              height: _dragOffset,
              alignment: Alignment.center,
              child: _buildIndicator(),
            ),
          ),
      ],
    );
  }

  Widget _buildIndicator() {
    if (_isRefreshing) {
      return AnimatedBuilder(
        animation: _spinController,
        builder: (context, _) {
          return CustomPaint(
            size: const Size(26, 26),
            painter: _MlRefreshSpinnerPainter(
              progress: 0.75,
              rotation: _spinController.value * 2 * math.pi,
              color: MlColors.primary,
            ),
          );
        },
      );
    }

    final pullProgress = math.min(1.0, _dragOffset / widget.displacement);
    return CustomPaint(
      size: const Size(26, 26),
      painter: _MlRefreshSpinnerPainter(
        progress: pullProgress,
        rotation: pullProgress * math.pi,
        color: pullProgress >= 1.0 ? MlColors.primary : MlColors.textMuted,
      ),
    );
  }
}

class _MlRefreshSpinnerPainter extends CustomPainter {
  const _MlRefreshSpinnerPainter({
    required this.progress,
    required this.rotation,
    required this.color,
  });

  final double progress;
  final double rotation;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final strokeWidth = 3.0;
    final center = Offset(size.width / 2, size.height / 2);
    final radius = (size.width - strokeWidth) / 2;

    final bgPaint = Paint()
      ..color = MlColors.border
      ..style = PaintingStyle.stroke
      ..strokeWidth = strokeWidth;
    canvas.drawCircle(center, radius, bgPaint);

    final sweepPaint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeWidth = strokeWidth;

    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(rotation);
    canvas.translate(-center.dx, -center.dy);

    final sweepAngle = 2 * math.pi * progress * 0.8;
    canvas.drawArc(
      Rect.fromCircle(center: center, radius: radius),
      -math.pi / 2,
      sweepAngle,
      false,
      sweepPaint,
    );

    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _MlRefreshSpinnerPainter oldDelegate) {
    return oldDelegate.progress != progress ||
        oldDelegate.rotation != rotation ||
        oldDelegate.color != color;
  }
}

