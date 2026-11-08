import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';

/// Smooth, pulsing skeleton placeholder for loading states.
class MlSkeleton extends StatefulWidget {
  const MlSkeleton({
    super.key,
    this.width,
    this.height = 16.0,
    this.borderRadius = MlRadii.md,
  });

  const MlSkeleton.circle({
    super.key,
    required double size,
  })  : width = size,
        height = size,
        borderRadius = 999.0;

  final double? width;
  final double height;
  final double borderRadius;

  @override
  State<MlSkeleton> createState() => _MlSkeletonState();
}

class _MlSkeletonState extends State<MlSkeleton> with SingleTickerProviderStateMixin {
  late AnimationController _controller;
  late Animation<double> _opacityAnimation;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1100),
    )..repeat(reverse: true);

    _opacityAnimation = Tween<double>(begin: 0.35, end: 0.85).animate(
      CurvedAnimation(parent: _controller, curve: Curves.easeInOut),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _opacityAnimation,
      builder: (context, child) {
        return Container(
          width: widget.width,
          height: widget.height,
          decoration: BoxDecoration(
            color: MlColors.surfaceLight.withOpacity(_opacityAnimation.value),
            borderRadius: BorderRadius.circular(widget.borderRadius),
          ),
        );
      },
    );
  }
}

