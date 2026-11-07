import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'sheet.dart';

class MlSelectOption<T> {
  const MlSelectOption({
    required this.value,
    required this.label,
    this.subtitle,
    this.icon,
  });

  final T value;
  final String label;
  final String? subtitle;
  final Widget? icon;
}

/// Custom select input triggering a bottom options sheet.
class MlSelect<T> extends StatelessWidget {
  const MlSelect({
    super.key,
    required this.options,
    this.value,
    this.onChanged,
    this.label,
    this.placeholder = 'Select…',
    this.errorText,
    this.enabled = true,
  });

  final List<MlSelectOption<T>> options;
  final T? value;
  final ValueChanged<T>? onChanged;
  final String? label;
  final String placeholder;
  final String? errorText;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    final selectedOption = options.cast<MlSelectOption<T>?>().firstWhere(
          (opt) => opt?.value == value,
          orElse: () => null,
        );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (label != null) ...[
          Text(label!, style: MlTypography.label(color: MlColors.textSecondary)),
          const SizedBox(height: MlSpacing.xxs),
        ],
        GestureDetector(
          onTap: enabled ? () => _openSheet(context) : null,
          child: Container(
            constraints: const BoxConstraints(minHeight: MlSpacing.minTouchTarget),
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
            decoration: BoxDecoration(
              color: enabled ? MlColors.surface : MlColors.surfaceElevated,
              borderRadius: MlRadii.borderMd,
              border: Border.all(
                color: errorText != null ? MlColors.danger : MlColors.border,
                width: 1.5,
              ),
            ),
            child: Row(
              children: [
                if (selectedOption?.icon != null) ...[
                  selectedOption!.icon!,
                  const SizedBox(width: MlSpacing.xs),
                ],
                Expanded(
                  child: Text(
                    selectedOption?.label ?? placeholder,
                    style: selectedOption != null
                        ? MlTypography.bodyLarge()
                        : MlTypography.bodyMedium(color: MlColors.textMuted),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                const Text('▼', style: TextStyle(color: MlColors.textMuted, fontSize: 10)),
              ],
            ),
          ),
        ),
        if (errorText != null) ...[
          const SizedBox(height: MlSpacing.xxs),
          Text(errorText!, style: MlTypography.caption(color: MlColors.danger)),
        ],
      ],
    );
  }

  void _openSheet(BuildContext context) {
    MlSheet.show(
      context: context,
      title: label ?? 'Choose Option',
      builder: (ctx) => Column(
        mainAxisSize: MainAxisSize.min,
        children: options.map((opt) {
          final isSelected = opt.value == value;
          return GestureDetector(
            onTap: () {
              Navigator.of(ctx).pop();
              onChanged?.call(opt.value);
            },
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 14),
              decoration: BoxDecoration(
                color: isSelected ? MlColors.surfaceElevated : const Color(0x00000000),
                border: const Border(bottom: BorderSide(color: MlColors.border, width: 0.5)),
              ),
              child: Row(
                children: [
                  if (opt.icon != null) ...[
                    opt.icon!,
                    const SizedBox(width: MlSpacing.sm),
                  ],
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(opt.label, style: MlTypography.bodyLarge()),
                        if (opt.subtitle != null)
                          Text(opt.subtitle!, style: MlTypography.caption()),
                      ],
                    ),
                  ),
                  if (isSelected)
                    const Text('✓', style: TextStyle(color: MlColors.primary, fontSize: 16)),
                ],
              ),
            ),
          );
        }).toList(),
      ),
    );
  }
}
