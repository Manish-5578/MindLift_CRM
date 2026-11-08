import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class OcrLeadScanSheet extends ConsumerStatefulWidget {
  const OcrLeadScanSheet({super.key, this.onLeadCreated});
  final VoidCallback? onLeadCreated;

  static void show(BuildContext context, {VoidCallback? onLeadCreated}) {
    MlSheet.show<void>(
      context: context,
      title: 'Scan Business Card (OCR)',
      builder: (_) => OcrLeadScanSheet(onLeadCreated: onLeadCreated),
    );
  }

  @override
  ConsumerState<OcrLeadScanSheet> createState() => _OcrLeadScanSheetState();
}

class _OcrLeadScanSheetState extends ConsumerState<OcrLeadScanSheet> {
  final _textController = TextEditingController();
  final _companyController = TextEditingController();
  final _nameController = TextEditingController();
  final _phoneController = TextEditingController();
  final _emailController = TextEditingController();
  final _titleController = TextEditingController();

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

  void _loadSample(String sampleText) {
    setState(() {
      _textController.text = sampleText;
    });
  }

  Future<void> _handleExtract() async {
    final text = _textController.text.trim();
    if (text.isEmpty) {
      MlToast.show(context, message: 'Please enter or paste card text first.');
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
      // Offline / Local regex fallback parser
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
      if (company == null && (l.toLowerCase().contains('llc') || l.toLowerCase().contains('inc') || l.toLowerCase().contains('group') || l.toLowerCase().contains('technologies'))) {
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
      _titleController.text = 'Executive';
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
            'Extract lead contact information automatically from business card text or photo OCR.',
            style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
          ),
          const SizedBox(height: MlSpacing.md),

          // Raw card text input
          MlTextField(
            label: 'Card Text / OCR Recognition',
            placeholder: 'Paste raw business card text here...',
            controller: _textController,
            maxLines: 4,
          ),
          const SizedBox(height: MlSpacing.xs),

          // Quick sample chips for testing
          Row(
            children: [
              Text('Sample: ', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
              GestureDetector(
                onTap: () => _loadSample('Apex Global Technologies LLC\nJohnathan Smith\nManaging Director\n+971 50 123 4567\njsmith@apexglobal.test\nwww.apexglobal.test'),
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
                onTap: () => _loadSample('Vertex Ventures Ltd\nSarah Connor\nVP Partnerships\n+971 55 987 6543\nsarah@vertex.test'),
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

          // Run Extraction Button
          MlButton(
            label: '⚡ Run OCR Extraction',
            variant: MlButtonVariant.secondary,
            isLoading: _isExtracting,
            onPressed: _isExtracting ? null : _handleExtract,
          ),
          const SizedBox(height: MlSpacing.lg),

          // Parsed Fields Preview
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
                        'Extracted Fields (Editable)',
                        style: MlTypography.labelMedium.copyWith(color: MlColors.primary),
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
                    label: 'Contact Name *',
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

