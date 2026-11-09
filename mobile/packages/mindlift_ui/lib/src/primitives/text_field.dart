import 'package:flutter/material.dart' show materialTextSelectionHandleControls;
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';
import '../tokens/strings.dart';
import '../tokens/radii.dart';
import '../tokens/spacing.dart';
import '../tokens/typography.dart';
import 'icons.dart';
import 'selection_toolbar.dart';

/// Bespoke input field replacing Material TextField.
class MlTextField extends StatefulWidget {
  const MlTextField({
    super.key,
    this.controller,
    this.initialValue,
    this.label,
    this.placeholder,
    this.helperText,
    this.errorText,
    this.obscureText = false,
    this.enabled = true,
    this.maxLines = 1,
    this.minLines,
    this.keyboardType,
    this.inputFormatters,
    this.onChanged,
    this.onSubmitted,
    this.prefix,
    this.suffix,
    this.focusNode,
    this.textInputAction,
    this.onEditingComplete,
  });

  final TextInputAction? textInputAction;
  final VoidCallback? onEditingComplete;
  final TextEditingController? controller;
  final String? initialValue;
  final String? label;
  final String? placeholder;
  final String? helperText;
  final String? errorText;
  final bool obscureText;
  final bool enabled;
  final int? maxLines;
  final int? minLines;
  final TextInputType? keyboardType;
  final List<TextInputFormatter>? inputFormatters;
  final ValueChanged<String>? onChanged;
  final ValueChanged<String>? onSubmitted;
  final Widget? prefix;
  final Widget? suffix;
  final FocusNode? focusNode;

  @override
  State<MlTextField> createState() => _MlTextFieldState();
}

class _MlTextFieldState extends State<MlTextField> {
  late TextEditingController _controller;
  late FocusNode _focusNode;
  bool _ownsController = false;
  bool _ownsFocusNode = false;
  bool _isFocused = false;

  @override
  void initState() {
    super.initState();
    if (widget.controller != null) {
      _controller = widget.controller!;
    } else {
      _controller = TextEditingController(text: widget.initialValue);
      _ownsController = true;
    }

    if (widget.focusNode != null) {
      _focusNode = widget.focusNode!;
    } else {
      _focusNode = FocusNode();
      _ownsFocusNode = true;
    }

    _focusNode.addListener(_handleFocusChange);
  }

  void _handleFocusChange() {
    setState(() => _isFocused = _focusNode.hasFocus);
  }

  @override
  void dispose() {
    _focusNode.removeListener(_handleFocusChange);
    if (_ownsController) _controller.dispose();
    if (_ownsFocusNode) _focusNode.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final hasError = widget.errorText != null;

    final borderColor = hasError
        ? MlColors.danger
        : (_isFocused ? MlColors.primary : MlColors.border);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (widget.label != null) ...[
          Text(
            MlStrings.t(widget.label!),
            style: MlTypography.label(
              color: hasError ? MlColors.danger : MlColors.textSecondary,
            ),
          ),
          const SizedBox(height: MlSpacing.xxs),
        ],
        Container(
          constraints: const BoxConstraints(minHeight: MlSpacing.minTouchTarget),
          padding: const EdgeInsets.symmetric(
            horizontal: MlSpacing.md,
            vertical: 10,
          ),
          decoration: BoxDecoration(
            color: widget.enabled ? MlColors.surface : MlColors.surfaceElevated,
            borderRadius: MlRadii.borderLg,
            border: Border.all(color: borderColor, width: _isFocused ? 2 : 1.2),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              if (widget.prefix != null) ...[
                widget.prefix!,
                const SizedBox(width: MlSpacing.xs),
              ],
              Expanded(
                child: Stack(
                  alignment: AlignmentDirectional.centerStart,
                  children: [
                    if (_controller.text.isEmpty && widget.placeholder != null)
                      Text(
                        MlStrings.t(widget.placeholder!),
                        style: MlTypography.bodyMedium(color: MlColors.textMuted),
                      ),
                    EditableText(
                      controller: _controller,
                      focusNode: _focusNode,
                      style: MlTypography.bodyLarge(
                        color: widget.enabled ? MlColors.textPrimary : MlColors.textDisabled,
                      ),
                      cursorColor: MlColors.primary,
                      cursorWidth: 2,
                      cursorRadius: const Radius.circular(2),
                      selectionColor: MlColors.primary.withOpacity(0.28),
                      selectionControls: materialTextSelectionHandleControls,
                      contextMenuBuilder: (context, editableTextState) => MlSelectionToolbar(state: editableTextState),
                      backgroundCursorColor: MlColors.surface,
                      obscureText: widget.obscureText,
                      keyboardType: widget.keyboardType,
                      inputFormatters: widget.inputFormatters,
                      maxLines: widget.maxLines,
                      minLines: widget.minLines,
                      onChanged: (text) {
                        setState(() {});
                        widget.onChanged?.call(text);
                      },
                      textInputAction: widget.textInputAction,
                      onEditingComplete: widget.onEditingComplete,
                      onSubmitted: widget.onSubmitted,
                      readOnly: !widget.enabled,
                    ),
                  ],
                ),
              ),
              if (widget.suffix != null) ...[
                const SizedBox(width: MlSpacing.xs),
                widget.suffix!,
              ],
            ],
          ),
        ),
        if (widget.errorText != null) ...[
          const SizedBox(height: MlSpacing.xxs),
          Text(
            MlStrings.t(widget.errorText!),
            style: MlTypography.caption(color: MlColors.danger),
          ),
        ] else if (widget.helperText != null) ...[
          const SizedBox(height: MlSpacing.xxs),
          Text(
            MlStrings.t(widget.helperText!),
            style: MlTypography.caption(color: MlColors.textMuted),
          ),
        ],
      ],
    );
  }
}

/// Specialized search field with clear button.
class MlSearchField extends StatelessWidget {
  const MlSearchField({
    super.key,
    this.controller,
    this.placeholder = 'Search…',
    this.onChanged,
    this.onClear,
  });

  final TextEditingController? controller;
  final String placeholder;
  final ValueChanged<String>? onChanged;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) {
    return MlTextField(
      controller: controller,
      placeholder: placeholder,
      onChanged: onChanged,
      prefix: MlIcon(MlIcons.search, size: 20, color: MlColors.textMuted),
      suffix: controller?.text.isNotEmpty == true
          ? GestureDetector(
              onTap: () {
                controller?.clear();
                onChanged?.call('');
                onClear?.call();
              },
              child: MlIcon(MlIcons.close, size: 18, color: MlColors.textMuted),
            )
          : null,
    );
  }
}

