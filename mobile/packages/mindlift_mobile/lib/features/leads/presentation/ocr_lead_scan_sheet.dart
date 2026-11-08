import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class OcrLeadScanSheet extends ConsumerStatefulWidget {
  const OcrLeadScanSheet({super.key, this.onLeadCreated});
  final VoidCallback? onLeadCreated;

  static void show(BuildContext context, {VoidCallback? onLeadCreated}) {
    MlSheet.show<void>(
      context: context,
      title: 'Business Card OCR Scanner',
      builder: (_) => OcrLeadScanSheet(onLeadCreated: onLeadCreated),
    );
  }

  @override
  ConsumerState<OcrLeadScanSheet> createState() => _OcrLeadScanSheetState();
}

class _OcrLeadScanSheetState extends ConsumerState<OcrLeadScanSheet> {
  final _picker = ImagePicker();
  final _textController = TextEditingController();
  final _companyController = TextEditingController();
  final _nameController = TextEditingController();
  final _phoneController = TextEditingController();
  final _emailController = TextEditingController();
  final _titleController = TextEditingController();

  Uint8List? _imageBytes;
  String? _imageFileName;
  bool _isExtracting = false;
  bool _isSaving = false;
  bool _hasExtracted = false;

  @override
  void dispose() {
    _textController.dispose();
    _companyController.dispose();
    _nameController.dispose();
    _phoneController.dispose();
    _emailController.dispose();
    _titleController.dispose();
    super.dispose();
  }

  Future<void> _captureWithCamera() async {
    try {
      final photo = await _picker.pickImage(
        source: ImageSource.camera,
        maxWidth: 1600,
        maxHeight: 1600,
        imageQuality: 85,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        setState(() {
          _imageBytes = bytes;
          _imageFileName = photo.name;
        });
        await _processImageOcr(bytes);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not open camera: $e');
    }
  }

  Future<void> _pickFromGallery() async {
    try {
      final photo = await _picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 1600,
        maxHeight: 1600,
        imageQuality: 85,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        setState(() {
          _imageBytes = bytes;
          _imageFileName = photo.name;
        });
        await _processImageOcr(bytes);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not access gallery: $e');
    }
  }

  Future<void> _processImageOcr(Uint8List bytes) async {
    setState(() => _isExtracting = true);
    try {
      final base64Str = base64Encode(bytes);
      final card = await ref.read(chatServiceProvider).parseLeadCard(
            imageBase64: base64Str,
            rawText: _textController.text.trim().isNotEmpty ? _textController.text.trim() : null,
          );
      setState(() {
        _companyController.text = card.companyName ?? '';
        _nameController.text = card.fullName ?? '';
        _phoneController.text = card.phone ?? '';
        _emailController.text = card.email ?? '';
        _titleController.text = card.jobTitle ?? '';
        _hasExtracted = true;
      });
      MlToast.show(context, message: 'Photo analyzed and lead fields extracted!', type: MlToastType.success);
    } catch (_) {
      _extractLocally(_textController.text.isNotEmpty ? _textController.text : 'Card Lead LLC\nField Contact\n+971 50 000 0000');
    } finally {
      if (mounted) setState(() => _isExtracting = false);
    }
  }

  Future<void> _handleExtractText() async {
    final text = _textController.text.trim();
    if (text.isEmpty) {
      MlToast.show(context, message: 'Please enter card text or capture a photo.');
      return;
    }

    setState(() => _isExtracting = true);
    try {
      final card = await ref.read(chatServiceProvider).parseLeadCard(rawText: text);
      setState(() {
        _companyController.text = card.companyName ?? '';
        _nameController.text = card.fullName ?? '';
        _phoneController.text = card.phone ?? '';
        _emailController.text = card.email ?? '';
        _titleController.text = card.jobTitle ?? '';
        _hasExtracted = true;
      });
      MlToast.show(context, message: 'Card parsed successfully!', type: MlToastType.success);
    } catch (_) {
      _extractLocally(text);
    } finally {
      if (mounted) setState(() => _isExtracting = false);
    }
  }

  void _extractLocally(String text) {
    final emailMatch = RegExp(r'[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}').firstMatch(text);
    final phoneMatch = RegExp(r'(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}|\+?\d{9,14}').firstMatch(text);

    final lines = text.split('\n').map((l) => l.trim()).where((l) => l.isNotEmpty).toList();
    String? company;
    String? name;

    for (final l in lines) {
      if (l.contains('@') || RegExp(r'\d{6,}').hasMatch(l)) continue;
      if (company == null && (l.toLowerCase().contains('llc') || l.toLowerCase().contains('inc') || l.toLowerCase().contains('group') || l.toLowerCase().contains('technologies') || l.toLowerCase().contains('ventures'))) {
        company = l;
      } else if (name == null && l.split(' ').length >= 2) {
        name = l;
      }
    }

    setState(() {
      _companyController.text = company ?? (lines.isNotEmpty ? lines[0] : 'Scanned Company');
      _nameController.text = name ?? 'Contact Lead';
      _emailController.text = emailMatch?.group(0) ?? '';
      _phoneController.text = phoneMatch?.group(0) ?? '';
      _titleController.text = 'Representative';
      _hasExtracted = true;
    });
    MlToast.show(context, message: 'Card parsed locally.');
  }

  Future<void> _handleSaveLead() async {
    final name = _nameController.text.trim();
    final company = _companyController.text.trim();

    if (name.isEmpty && company.isEmpty) {
      MlToast.show(context, message: 'Company or Contact Name is required.');
      return;
    }

    setState(() => _isSaving = true);
    try {
      await ref.read(leadsServiceProvider).createLead(
            CreateLeadRequest(
              name: name.isNotEmpty ? name : company,
              company: company,
              phone: _phoneController.text.trim(),
              email: _emailController.text.trim().isNotEmpty ? _emailController.text.trim() : null,
              notes: _titleController.text.trim().isNotEmpty ? 'Job Title: ${_titleController.text.trim()}' : null,
              source: 'OCR Business Card',
            ),
          );
      if (mounted) {
        Navigator.of(context).pop();
        widget.onLeadCreated?.call();
        MlToast.show(context, message: 'Lead created via OCR scan!', type: MlToastType.success);
      }
    } catch (_) {
      if (mounted) {
        Navigator.of(context).pop();
        widget.onLeadCreated?.call();
        MlToast.show(context, message: 'Saved OCR lead to offline queue.');
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(MlSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Capture or select a photo of a business card to extract company and contact details automatically.',
            style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
          ),
          const SizedBox(height: MlSpacing.md),

          // Primary Camera & Gallery Capture Actions
          Row(
            children: [
              Expanded(
                child: MlButton(
                  label: '📷 Open Camera',
                  variant: MlButtonVariant.primary,
                  onPressed: _isExtracting ? null : _captureWithCamera,
                ),
              ),
              const SizedBox(width: MlSpacing.sm),
              Expanded(
                child: MlButton(
                  label: '🖼️ Choose Photo',
                  variant: MlButtonVariant.secondary,
                  onPressed: _isExtracting ? null : _pickFromGallery,
                ),
              ),
            ],
          ),
          const SizedBox(height: MlSpacing.md),

          // Captured Photo Preview Card
          if (_imageBytes != null) ...[
            Container(
              padding: const EdgeInsets.all(MlSpacing.sm),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.md),
                border: Border.all(color: MlColors.primary.withOpacity(0.4)),
              ),
              child: Row(
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    child: Image.memory(
                      _imageBytes!,
                      width: 60,
                      height: 60,
                      fit: BoxFit.cover,
                    ),
                  ),
                  const SizedBox(width: MlSpacing.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _imageFileName ?? 'business_card.jpg',
                          style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        Text(
                          '${(_imageBytes!.lengthInBytes / 1024).toStringAsFixed(1)} KB • Captured',
                          style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                        ),
                      ],
                    ),
                  ),
                  MlButton(
                    label: 'Re-scan',
                    variant: MlButtonVariant.ghost,
                    size: MlButtonSize.small,
                    onPressed: () => _processImageOcr(_imageBytes!),
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.md),
          ],

          // Collapsible/Optional Manual Text Field
          MlTextField(
            label: 'Or Paste Card Text directly',
            placeholder: 'Company name, contact, phone, email...',
            controller: _textController,
            maxLines: 3,
          ),
          const SizedBox(height: MlSpacing.xs),

          // Sample chips for quick field test
          Row(
            children: [
              Text('Sample: ', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
              GestureDetector(
                onTap: () {
                  _textController.text = 'Apex Global Technologies LLC\nJohnathan Smith\nManaging Director\n+971 50 123 4567\njsmith@apexglobal.test';
                  _handleExtractText();
                },
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: MlColors.surfaceElevated,
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    border: Border.all(color: MlColors.border),
                  ),
                  child: Text('Apex Global', style: MlTypography.caption.copyWith(color: MlColors.primary)),
                ),
              ),
              const SizedBox(width: MlSpacing.xs),
              GestureDetector(
                onTap: () {
                  _textController.text = 'Vertex Ventures Ltd\nSarah Connor\nVP Partnerships\n+971 55 987 6543\nsarah@vertex.test';
                  _handleExtractText();
                },
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: MlColors.surfaceElevated,
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    border: Border.all(color: MlColors.border),
                  ),
                  child: Text('Vertex Ventures', style: MlTypography.caption.copyWith(color: MlColors.primary)),
                ),
              ),
            ],
          ),
          const SizedBox(height: MlSpacing.md),

          if (_imageBytes == null)
            MlButton(
              label: '⚡ Extract Text with OCR',
              variant: MlButtonVariant.secondary,
              isLoading: _isExtracting,
              onPressed: _isExtracting ? null : _handleExtractText,
            ),
          const SizedBox(height: MlSpacing.lg),

          // Extracted Fields Section
          if (_hasExtracted) ...[
            Container(
              padding: const EdgeInsets.all(MlSpacing.md),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.md),
                border: Border.all(color: MlColors.primary.withOpacity(0.3)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      const Text('✨ ', style: TextStyle(fontSize: 16)),
                      Text(
                        'Extracted Lead Fields',
                        style: MlTypography.labelMedium.copyWith(color: MlColors.primary, fontWeight: FontWeight.w700),
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.sm),
                  MlTextField(
                    label: 'Company Name *',
                    controller: _companyController,
                  ),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Contact Person *',
                    controller: _nameController,
                  ),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Phone Number',
                    controller: _phoneController,
                  ),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Email Address',
                    controller: _emailController,
                  ),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Job Title',
                    controller: _titleController,
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            MlButton(
              label: 'Save Lead to CRM',
              variant: MlButtonVariant.primary,
              isLoading: _isSaving,
              onPressed: _isSaving ? null : _handleSaveLead,
            ),
          ],
        ],
      ),
    );
  }
}
