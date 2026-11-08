import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../offline/offline_database.dart';

class OcrLeadScanSheet extends ConsumerStatefulWidget {
  const OcrLeadScanSheet({super.key, this.onLeadCreated});
  final VoidCallback? onLeadCreated;

  static void show(BuildContext context, {VoidCallback? onLeadCreated}) {
    MlSheet.show<void>(
      context: context,
      title: 'Business Card Document OCR',
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
  String? _imageFilePath;
  bool _isExtracting = false;
  bool _isSaving = false;
  bool _hasExtracted = false;

  LeadDuplicateCandidateDto? _duplicateCandidate;
  double? _overallConfidence;
  String _detectedScript = 'Latin';

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
        maxWidth: 3200,
        maxHeight: 3200,
        imageQuality: 98,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        _validateAndProcessPhoto(photo.path, photo.name, bytes);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not access camera: $e', type: MlToastType.error);
    }
  }

  Future<void> _pickFromGallery() async {
    try {
      final photo = await _picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 3200,
        maxHeight: 3200,
        imageQuality: 98,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        _validateAndProcessPhoto(photo.path, photo.name, bytes);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not access gallery: $e', type: MlToastType.error);
    }
  }

  void _validateAndProcessPhoto(String path, String name, Uint8List bytes) {
    if (bytes.lengthInBytes < 15000) {
      MlToast.show(
        context,
        message: 'Image appears very small or dark. Ensure the card is well-lit and in focus.',
        type: MlToastType.info,
      );
    }

    setState(() {
      _imageBytes = bytes;
      _imageFileName = name;
      _imageFilePath = path;
      _duplicateCandidate = null;
    });

    _processImageOcr(path, bytes);
  }

  Future<void> _processImageOcr(String? filePath, Uint8List bytes) async {
    setState(() => _isExtracting = true);
    try {
      final base64Str = base64Encode(bytes);

      // 1. Authoritative Server OCR with deskewing, Arabic/English ensemble and CRM duplicate matching
      try {
        final card = await ref.read(chatServiceProvider).parseLeadCard(
              imageBase64: base64Str,
              rawText: _textController.text.trim().isNotEmpty ? _textController.text.trim() : null,
            );

        if (mounted) {
          setState(() {
            _companyController.text = card.companyName ?? '';
            _nameController.text = card.fullName ?? '';
            _phoneController.text = card.phone ?? '';
            _emailController.text = card.email ?? '';
            _titleController.text = card.jobTitle ?? '';
            _textController.text = card.rawText;
            _overallConfidence = card.overallConfidence;
            _detectedScript = card.detectedScript;
            _duplicateCandidate = card.duplicate;
            _hasExtracted = true;
          });

          if (card.duplicate != null) {
            MlToast.show(
              context,
              message: 'Potential duplicate found in CRM. Review details below.',
              type: MlToastType.info,
            );
          } else {
            MlToast.show(
              context,
              message: 'Server OCR complete (${card.detectedScript}, ${(card.overallConfidence * 100).toInt()}% conf).',
              type: MlToastType.success,
            );
          }
        }
        return;
      } catch (serverErr) {
        debugPrint('Server OCR failed or offline, falling back to on-device vision: $serverErr');
      }

      // 2. Offline fallback: Run on-device Google ML Kit text recognition
      String localText = '';
      if (filePath != null && filePath.isNotEmpty) {
        try {
          final inputImage = InputImage.fromFilePath(filePath);
          final textRecognizer = TextRecognizer(script: TextRecognitionScript.latin);
          final result = await textRecognizer.processImage(inputImage);
          await textRecognizer.close();
          localText = result.text.trim();
        } catch (mlErr) {
          debugPrint('MLKit offline extraction note: $mlErr');
        }
      }

      if (localText.isNotEmpty) {
        _textController.text = localText;
        _extractLocally(localText);
      } else if (_textController.text.trim().isNotEmpty) {
        _extractLocally(_textController.text.trim());
      } else {
        if (mounted) {
          MlToast.show(
            context,
            message: 'No readable text could be recognized from the card. Ensure adequate lighting and clear text.',
            type: MlToastType.info,
          );
        }
      }
    } catch (e) {
      if (mounted) {
        MlToast.show(context, message: 'OCR analysis failed: $e', type: MlToastType.error);
      }
    } finally {
      if (mounted) setState(() => _isExtracting = false);
    }
  }

  Future<void> _handleExtractText() async {
    final text = _textController.text.trim();
    if (text.isEmpty) {
      MlToast.show(context, message: 'Please enter card text or capture a photo.', type: MlToastType.info);
      return;
    }

    setState(() => _isExtracting = true);
    try {
      final card = await ref.read(chatServiceProvider).parseLeadCard(rawText: text);
      if (mounted) {
        setState(() {
          _companyController.text = card.companyName ?? '';
          _nameController.text = card.fullName ?? '';
          _phoneController.text = card.phone ?? '';
          _emailController.text = card.email ?? '';
          _titleController.text = card.jobTitle ?? '';
          _overallConfidence = card.overallConfidence;
          _detectedScript = card.detectedScript;
          _duplicateCandidate = card.duplicate;
          _hasExtracted = true;
        });
        MlToast.show(context, message: 'Card text parsed successfully!', type: MlToastType.success);
      }
    } catch (_) {
      _extractLocally(text);
    } finally {
      if (mounted) setState(() => _isExtracting = false);
    }
  }

  void _extractLocally(String text) {
    final trimmed = text.trim();
    if (trimmed.isEmpty) {
      MlToast.show(context, message: 'No readable card text found to parse.', type: MlToastType.info);
      return;
    }

    final emailMatch = RegExp(r'[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}').firstMatch(trimmed);
    final phoneMatch = RegExp(r'(?:\+?\d{1,4}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}|\+?\d{8,15}').firstMatch(trimmed);

    final lines = trimmed.split(RegExp(r'\r?\n')).map((l) => l.trim()).where((l) => l.isNotEmpty).toList();
    String? company;
    String? name;
    String? title;

    final companyKeywords = [
      'llc', 'inc', 'corp', 'corporation', 'ltd', 'limited', 'group', 'solutions',
      'technologies', 'tech', 'co.', 'company', 'holdings', 'ventures', 'services',
      'industries', 'consulting', 'enterprises', 'global', 'international', 'agency',
      'ذ.م.م', 'ش.م.ح', 'شركة', 'مؤسسة', 'مجموعة'
    ];
    final titleKeywords = [
      'director', 'manager', 'ceo', 'cto', 'cfo', 'founder', 'president', 'consultant',
      'head', 'lead', 'developer', 'engineer', 'specialist', 'officer', 'representative', 'architect',
      'مدير عام', 'رئيس تنفيذي', 'مؤسس', 'مهندس', 'مستشار'
    ];

    final remainingLines = <String>[];

    for (final l in lines) {
      final lower = l.toLowerCase();
      if (l.contains('@') || RegExp(r'\d{7,}').hasMatch(l)) {
        continue;
      }
      if (title == null && titleKeywords.any((k) => lower.contains(k))) {
        title = l;
        continue;
      }
      if (company == null && companyKeywords.any((k) => lower.contains(k))) {
        company = l;
        continue;
      }
      if (lower.contains('street') || lower.contains('road') || lower.contains('tower') ||
          lower.contains('building') || lower.contains('box') || lower.contains('dubai') ||
          lower.contains('floor') || lower.contains('uae') || lower.contains('شارع') || lower.contains('برج')) {
        continue;
      }
      remainingLines.add(l);
    }

    for (final l in remainingLines) {
      final words = l.split(' ').where((w) => w.isNotEmpty).toList();
      if (name == null && words.length >= 2 && words.length <= 4 && !words.any((w) => RegExp(r'\d').hasMatch(w))) {
        name = l;
      } else if (company == null && words.length >= 1 && words.length <= 6) {
        company = l;
      }
    }

    final hasArabic = RegExp(r'[\u0600-\u06FF]').hasMatch(trimmed);
    final hasLatin = RegExp(r'[a-zA-Z]').hasMatch(trimmed);

    setState(() {
      _companyController.text = company ?? '';
      _nameController.text = name ?? '';
      _emailController.text = emailMatch?.group(0) ?? '';
      _phoneController.text = phoneMatch?.group(0) ?? '';
      _titleController.text = title ?? '';
      _detectedScript = (hasArabic && hasLatin) ? 'Bilingual' : (hasArabic ? 'Arabic' : 'Latin');
      _overallConfidence = 0.75;
      _duplicateCandidate = null;
      _hasExtracted = true;
    });
    MlToast.show(context, message: 'Card parsed locally ($_detectedScript).', type: MlToastType.info);
  }

  Future<void> _handleSaveLead() async {
    final name = _nameController.text.trim();
    final company = _companyController.text.trim();

    if (name.isEmpty && company.isEmpty) {
      MlToast.show(context, message: 'Company Name or Contact Person is required.', type: MlToastType.error);
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
              source: 'Mobile Business Card Scan',
            ),
          );
      if (mounted) {
        Navigator.of(context).pop();
        widget.onLeadCreated?.call();
        MlToast.show(context, message: 'Lead created successfully from card scan!', type: MlToastType.success);
      }
    } catch (e) {
      if (e is DioException) {
        final isNetworkDisconnected = e.type == DioExceptionType.connectionError ||
            e.type == DioExceptionType.connectionTimeout ||
            e.type == DioExceptionType.sendTimeout ||
            e.type == DioExceptionType.receiveTimeout;

        if (isNetworkDisconnected) {
          // Durable outbox persistence
          final tempId = 'lead_${DateTime.now().millisecondsSinceEpoch}';
          await ref.read(offlineDatabaseProvider).queueMutation(
                LocalOutboxMutation(
                  clientMutationId: tempId,
                  entityType: 'Lead',
                  operation: 'create',
                  clientTempId: tempId,
                  payloadJson: jsonEncode({
                    'fullName': name.isNotEmpty ? name : company,
                    'companyName': company,
                    'phone': _phoneController.text.trim(),
                    'email': _emailController.text.trim().isNotEmpty ? _emailController.text.trim() : null,
                    'notes': _titleController.text.trim().isNotEmpty ? 'Job Title: ${_titleController.text.trim()}' : null,
                    'source': 'Mobile Business Card Scan',
                  }),
                  createdAtUtc: DateTime.now().toUtc(),
                ),
              );

          if (mounted) {
            Navigator.of(context).pop();
            widget.onLeadCreated?.call();
            MlToast.show(
              context,
              message: 'Network offline: Lead safely queued in outbox (ID: ${tempId.substring(0, 8)}).',
              type: MlToastType.info,
            );
          }
          return;
        }

        // Server API failure: DO NOT pop sheet. Display specific error message so user does not lose input.
        String errorMsg = 'Failed to create lead.';
        final status = e.response?.statusCode;
        if (status == 403) {
          errorMsg = 'Permission Denied: You do not have permission to create leads.';
        } else if (status == 409) {
          errorMsg = 'Duplicate Record: A lead or contact with this email or phone already exists.';
        } else if (status == 400) {
          final data = e.response?.data;
          if (data is Map && data['message'] != null) {
            errorMsg = 'Validation Error: ${data['message']}';
          } else {
            errorMsg = 'Validation Error: Please verify the required lead fields.';
          }
        } else if (status != null && status >= 500) {
          errorMsg = 'Server Error ($status): Could not save lead. Please retry.';
        }

        if (mounted) {
          MlToast.show(context, message: errorMsg, type: MlToastType.error);
        }
        return;
      }

      if (mounted) {
        MlToast.show(context, message: 'Could not create lead: $e', type: MlToastType.error);
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
            'Capture or select a business card image to extract CRM contact and company details automatically.',
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
                    onPressed: () => _processImageOcr(_imageFilePath, _imageBytes!),
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.md),
          ],

          // Potential Duplicate CRM Alert
          if (_duplicateCandidate != null) ...[
            Container(
              padding: const EdgeInsets.all(MlSpacing.sm),
              decoration: BoxDecoration(
                color: const Color(0xFFFEF3C7),
                borderRadius: BorderRadius.circular(MlRadii.sm),
                border: Border.all(color: const Color(0xFFF59E0B)),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text('⚠️ ', style: TextStyle(fontSize: 16)),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Existing ${_duplicateCandidate!.recordType} Found in CRM',
                          style: MlTypography.caption.copyWith(
                            color: const Color(0xFF92400E),
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          'Name: ${_duplicateCandidate!.name}${_duplicateCandidate!.company != null ? " • Company: ${_duplicateCandidate!.company}" : ""}',
                          style: MlTypography.caption.copyWith(color: const Color(0xFF78350F)),
                        ),
                        if (_duplicateCandidate!.email != null || _duplicateCandidate!.phone != null)
                          Text(
                            'Matching: ${_duplicateCandidate!.email ?? _duplicateCandidate!.phone}',
                            style: MlTypography.caption.copyWith(color: const Color(0xFF78350F)),
                          ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.md),
          ],

          // Manual Text Field / Extracted Raw Text
          MlTextField(
            label: 'Recognized Text / Manual Card Input',
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
                  _textController.text = 'Apex Global Technologies LLC\nJohnathan Smith\nManaging Director\n+971 50 123 4567\njsmith@apexglobal.test\nDubai, UAE';
                  _handleExtractText();
                },
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: MlColors.surfaceElevated,
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    border: Border.all(color: MlColors.border),
                  ),
                  child: Text('English Card', style: MlTypography.caption.copyWith(color: MlColors.primary)),
                ),
              ),
              const SizedBox(width: MlSpacing.xs),
              GestureDetector(
                onTap: () {
                  _textController.text = 'شركة النور للتكنولوجيا ذ.م.م\nمحمد عبدالله\nالمدير العام\n+971 4 333 4444\ninfo@alnoortech.ae\nشارع الشيخ زايد، دبي';
                  _handleExtractText();
                },
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: MlColors.surfaceElevated,
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    border: Border.all(color: MlColors.border),
                  ),
                  child: Text('بطاقة عربية', style: MlTypography.caption.copyWith(color: MlColors.primary)),
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
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
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
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                        decoration: BoxDecoration(
                          color: MlColors.primary.withOpacity(0.1),
                          borderRadius: BorderRadius.circular(MlRadii.sm),
                        ),
                        child: Text(
                          '$_detectedScript • ${((_overallConfidence ?? 0.8) * 100).toInt()}% conf',
                          style: MlTypography.caption.copyWith(color: MlColors.primary, fontWeight: FontWeight.w600),
                        ),
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
              label: _duplicateCandidate != null ? 'Create Lead Anyway (Duplicate Warning)' : 'Save Lead to CRM',
              variant: _duplicateCandidate != null ? MlButtonVariant.secondary : MlButtonVariant.primary,
              isLoading: _isSaving,
              onPressed: _isSaving ? null : _handleSaveLead,
            ),
          ],
        ],
      ),
    );
  }
}
