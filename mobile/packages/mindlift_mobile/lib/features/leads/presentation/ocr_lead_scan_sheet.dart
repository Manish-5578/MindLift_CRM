import 'dart:convert';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../offline/offline_database.dart';
import 'card_line_classifier.dart';

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
  final _addressController = TextEditingController();

  // Front side (required)
  Uint8List? _frontBytes;
  String? _frontFileName;
  String? _frontFilePath;

  // Back side (optional)
  Uint8List? _backBytes;
  String? _backFileName;
  String? _backFilePath;

  // Legacy alias for backward compat (review overlay uses front image)
  Uint8List? get _imageBytes => _frontBytes;
  String? get _imageFileName => _frontFileName;

  bool _isExtracting = false;
  bool _isSaving = false;
  bool _hasExtracted = false;

  LeadDuplicateCandidateDto? _duplicateCandidate;
  double? _overallConfidence;
  double? _engineConfidence;
  double? _nameConfidence;
  double? _addressConfidence;
  double? _companyConfidence;
  double? _phoneConfidence;
  double? _emailConfidence;
  double? _titleConfidence;
  List<CardFieldConflictDto> _conflicts = [];
  List<String> _lowFields = [];
  String _detectedScript = 'Latin';

  List<CardEvidenceBoxDto> _evidence = [];
  bool _showReviewMode = false;
  bool _requiresReview = false;
  bool _isReviewConfirmed = false;

  static bool _isAddressText(String line, {bool hasCompanyWord = false}) =>
      CardLineClassifier.isAddressText(line, hasCompanyWord: hasCompanyWord);

  /// True if the name field appears to hold an address line (or anything with digits or a colon, which a name never has).
  bool get _nameIsLikelyAddress {
    final val = _nameController.text.trim();
    if (val.isEmpty) return false;
    return val.contains(':') || RegExp(r'\d').hasMatch(val) || _isAddressText(val);
  }

  List<CardEvidenceBoxDto> get _frontEvidence => _evidence.where((b) => b.side != 'back').toList();

  @override
  void dispose() {
    _textController.dispose();
    _companyController.dispose();
    _nameController.dispose();
    _phoneController.dispose();
    _emailController.dispose();
    _titleController.dispose();
    _addressController.dispose();
    super.dispose();
  }

  Future<void> _captureWithCamera({bool isBack = false}) async {
    try {
      final photo = await _picker.pickImage(
        source: ImageSource.camera,
        maxWidth: 3200,
        maxHeight: 3200,
        imageQuality: 98,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        _validateAndProcessPhoto(photo.path, photo.name, bytes, isBack: isBack);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not access camera: $e', type: MlToastType.error);
    }
  }

  Future<void> _pickFromGallery({bool isBack = false}) async {
    try {
      final photo = await _picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 3200,
        maxHeight: 3200,
        imageQuality: 98,
      );
      if (photo != null) {
        final bytes = await photo.readAsBytes();
        _validateAndProcessPhoto(photo.path, photo.name, bytes, isBack: isBack);
      }
    } catch (e) {
      MlToast.show(context, message: 'Could not access gallery: $e', type: MlToastType.error);
    }
  }

  void _validateAndProcessPhoto(String path, String name, Uint8List bytes, {bool isBack = false}) {
    if (isBack && _frontBytes == null) {
      MlToast.show(context, message: 'Capture the front of the card first.', type: MlToastType.info);
      return;
    }

    if (bytes.lengthInBytes < 15000) {
      MlToast.show(
        context,
        message: 'Image appears very small or dark. Ensure the card is well-lit and in focus.',
        type: MlToastType.info,
      );
    }

    setState(() {
      if (isBack) {
        _backBytes = bytes;
        _backFileName = name;
        _backFilePath = path;
      } else {
        _frontBytes = bytes;
        _frontFileName = name;
        _frontFilePath = path;
      }
      _duplicateCandidate = null;
      _evidence = [];
      _conflicts = [];
      _requiresReview = false;
      _isReviewConfirmed = false;
      _showReviewMode = false;
    });

    _scanSides();
  }

  void _removeBackSide() {
    setState(() {
      _backBytes = null;
      _backFileName = null;
      _backFilePath = null;
    });
    if (_frontBytes != null) _scanSides();
  }

  /// Reads the front (and the back, when there is one) on the server in one request. If the server cannot be reached, each side is read on the phone.
  Future<void> _scanSides() async {
    if (_frontBytes == null) return;
    setState(() => _isExtracting = true);
    try {
      try {
        final card = await ref.read(chatServiceProvider).scanCardSides(
              frontPath: _frontFilePath,
              frontBytes: _frontBytes,
              frontName: _frontFileName ?? 'card_front.jpg',
              backPath: _backFilePath,
              backBytes: _backBytes,
              backName: _backFileName ?? 'card_back.jpg',
            );

        if (mounted) {
          _applyCard(card);
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
        debugPrint('Server multipart OCR failed or offline, falling back to on-device vision: $serverErr');
      }

      // Offline fallback: Google ML Kit on each side.
      final texts = <String>[];
      for (final path in [_frontFilePath, _backFilePath]) {
        if (path == null || path.isEmpty) continue;
        try {
          final recognizer = TextRecognizer(script: TextRecognitionScript.latin);
          final result = await recognizer.processImage(InputImage.fromFilePath(path));
          await recognizer.close();
          if (result.text.trim().isNotEmpty) texts.add(result.text.trim());
        } catch (mlErr) {
          debugPrint('MLKit offline extraction note: $mlErr');
        }
      }

      final localText = texts.join('\n');
      if (localText.isNotEmpty) {
        _textController.text = localText;
        _extractLocally(localText);
      } else if (_textController.text.trim().isNotEmpty) {
        _extractLocally(_textController.text.trim());
      } else if (mounted) {
        MlToast.show(
          context,
          message: 'No readable text could be recognized from the card. Ensure adequate lighting and clear text.',
          type: MlToastType.info,
        );
      }
    } catch (e) {
      if (mounted) {
        MlToast.show(context, message: 'OCR analysis failed: $e', type: MlToastType.error);
      }
    } finally {
      if (mounted) setState(() => _isExtracting = false);
    }
  }

  /// Puts a server result into the form and decides whether the person has to check anything before saving.
  void _applyCard(ExtractedLeadCardDto card) {
    setState(() {
      _companyController.text = card.companyName ?? '';
      _nameController.text = card.fullName ?? '';
      _phoneController.text = card.phone ?? '';
      _emailController.text = card.email ?? '';
      _titleController.text = card.jobTitle ?? '';
      _addressController.text = card.address ?? '';
      _textController.text = card.rawText;
      _overallConfidence = card.overallConfidence;
      _engineConfidence = card.engineConfidence;
      _companyConfidence = card.companyConfidence;
      _nameConfidence = card.nameConfidence;
      _phoneConfidence = card.phoneConfidence;
      _emailConfidence = card.emailConfidence;
      _titleConfidence = card.titleConfidence;
      _addressConfidence = card.addressConfidence;
      _detectedScript = card.detectedScript;
      _duplicateCandidate = card.duplicate;
      _evidence = card.evidence ?? [];
      _conflicts = card.conflicts ?? [];
      _hasExtracted = true;
      _refreshReviewGate();
    });
  }

  /// A card is held for review only when a field that has a value is low-confidence, the two sides disagree, or the name looks like an address.
  /// Low-confidence words in a logo or tagline do not hold it.
  void _refreshReviewGate() {
    final low = <String>[];
    void check(String label, TextEditingController c, double? confidence) {
      if (c.text.trim().isNotEmpty && confidence != null && confidence < 0.75) low.add(label);
    }

    check('Company', _companyController, _companyConfidence);
    check('Contact person', _nameController, _nameConfidence);
    check('Phone', _phoneController, _phoneConfidence);
    check('Email', _emailController, _emailConfidence);
    check('Job title', _titleController, _titleConfidence);
    check('Address', _addressController, _addressConfidence);
    if (_nameIsLikelyAddress) low.add('Contact person looks like an address');
    for (final c in _conflicts) {
      low.add('${c.field} differs between front and back');
    }

    _lowFields = low;
    final needs = low.isNotEmpty;
    if (!needs) {
      _isReviewConfirmed = true;
    } else if (!_requiresReview) {
      _isReviewConfirmed = false;
    }
    _requiresReview = needs;
    _showReviewMode = needs && _frontEvidence.isNotEmpty;
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
        _applyCard(card);
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
    final phoneMatch = RegExp(r'(?:\+?\d{1,4}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}|\+?\d{8,15}');

    final lines = trimmed.split(RegExp(r'\r?\n')).map((l) => l.trim()).where((l) => l.isNotEmpty).toList();
    String? company;
    String? name;
    String? title;
    String? phone;
    final addressParts = <String>[];

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
      if (l.contains('@') || lower.startsWith('www.') || lower.startsWith('http')) {
        continue;
      }

      final hasCompanyWord = companyKeywords.any((k) => lower.contains(k));
      // An address is claimed before the phone pattern: "560 001" is a PIN code, not a phone number.
      if (_isAddressText(l, hasCompanyWord: hasCompanyWord)) {
        addressParts.add(l);
        continue;
      }

      final phoneHit = phoneMatch.firstMatch(l);
      if (phone == null && phoneHit != null && phoneHit.group(0)!.replaceAll(RegExp(r'\D'), '').length >= 7) {
        phone = phoneHit.group(0);
        continue;
      }
      if (title == null && titleKeywords.any((k) => lower.contains(k))) {
        title = l;
        continue;
      }
      if (company == null && hasCompanyWord) {
        company = l;
        continue;
      }
      remainingLines.add(l);
    }

    for (final l in remainingLines) {
      final words = l.split(' ').where((w) => w.isNotEmpty).toList();
      final looksLikeName = words.length >= 2 && words.length <= 4 && !RegExp(r'[\d:@]').hasMatch(l) && !_isAddressText(l);
      if (name == null && looksLikeName) {
        name = l;
      } else if (company == null && words.isNotEmpty && words.length <= 6) {
        company = l;
      }
    }

    final hasArabic = RegExp(r'[؀-ۿ]').hasMatch(trimmed);
    final hasLatin = RegExp(r'[a-zA-Z]').hasMatch(trimmed);

    setState(() {
      _companyController.text = company ?? '';
      _nameController.text = name ?? '';
      _emailController.text = emailMatch?.group(0) ?? '';
      _phoneController.text = phone ?? '';
      _titleController.text = title ?? '';
      _addressController.text = addressParts.join(', ');
      _detectedScript = (hasArabic && hasLatin) ? 'Bilingual' : (hasArabic ? 'Arabic' : 'Latin');
      _overallConfidence = 0.75;
      // A guess made on the phone is never presented as certain: the name and address are marked for a check.
      _nameConfidence = name != null ? 0.6 : null;
      _addressConfidence = addressParts.isNotEmpty ? 0.6 : null;
      _companyConfidence = null;
      _phoneConfidence = null;
      _emailConfidence = null;
      _titleConfidence = null;
      _conflicts = [];
      _duplicateCandidate = null;
      _hasExtracted = true;
      _refreshReviewGate();
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

    if (_nameIsLikelyAddress) {
      MlToast.show(
        context,
        message: 'Contact Person looks like an address. Move it to Address or correct the name.',
        type: MlToastType.error,
      );
      return;
    }

    if (_requiresReview && !_isReviewConfirmed) {
      setState(() => _showReviewMode = _frontEvidence.isNotEmpty);
      MlToast.show(
        context,
        message: 'Review Required: Low-confidence OCR detected. Please inspect and approve OCR review before saving.',
        type: MlToastType.info,
      );
      return;
    }

    final noteParts = <String>[];
    if (_titleController.text.trim().isNotEmpty) {
      noteParts.add('Job Title: ${_titleController.text.trim()}');
    }
    if (_requiresReview && _isReviewConfirmed) {
      noteParts.add('OCR Review: Manually inspected and confirmed by staff member (Low-confidence words verified).');
    }
    if (_duplicateCandidate != null) {
      noteParts.add(
        'Duplicate Override: Confirmed by staff member (Matched existing ${_duplicateCandidate!.recordType} "${_duplicateCandidate!.name}" [ID: ${_duplicateCandidate!.id}]).',
      );
    }
    final leadNotes = noteParts.isNotEmpty ? noteParts.join('\n') : null;

    setState(() => _isSaving = true);
    try {
      await ref.read(leadsServiceProvider).createLead(
            CreateLeadRequest(
              name: name.isNotEmpty ? name : company,
              company: company,
              phone: _phoneController.text.trim(),
              email: _emailController.text.trim().isNotEmpty ? _emailController.text.trim() : null,
              notes: leadNotes,
              address: _addressController.text.trim().isNotEmpty ? _addressController.text.trim() : null,
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
                    'notes': leadNotes,
                    'address': _addressController.text.trim().isNotEmpty ? _addressController.text.trim() : null,
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

  Widget _sideSlot({required bool isBack}) {
    final bytes = isBack ? _backBytes : _frontBytes;
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            isBack ? 'Back (optional)' : 'Front',
            style: MlTypography.labelMedium.copyWith(color: MlColors.textPrimary, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: MlSpacing.xs),
          Container(
            height: 72,
            decoration: BoxDecoration(
              color: MlColors.surface,
              borderRadius: BorderRadius.circular(MlRadii.sm),
              border: Border.all(color: bytes != null ? MlColors.primary.withOpacity(0.5) : MlColors.border),
            ),
            child: bytes != null
                ? ClipRRect(
                    borderRadius: BorderRadius.circular(MlRadii.sm),
                    child: Image.memory(bytes, fit: BoxFit.cover),
                  )
                : Center(
                    child: Text(
                      isBack ? 'Add back side' : 'Add front side',
                      style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                    ),
                  ),
          ),
          const SizedBox(height: MlSpacing.xs),
          Row(
            children: [
              Expanded(
                child: MlButton(
                  label: '📷 Camera',
                  variant: isBack ? MlButtonVariant.secondary : MlButtonVariant.primary,
                  size: MlButtonSize.small,
                  onPressed: _isExtracting ? null : () => _captureWithCamera(isBack: isBack),
                ),
              ),
              const SizedBox(width: MlSpacing.xs),
              Expanded(
                child: MlButton(
                  label: '🖼️ Photo',
                  variant: MlButtonVariant.secondary,
                  size: MlButtonSize.small,
                  onPressed: _isExtracting ? null : () => _pickFromGallery(isBack: isBack),
                ),
              ),
            ],
          ),
          if (isBack && _backBytes != null)
            MlButton(
              label: 'Remove back',
              variant: MlButtonVariant.ghost,
              size: MlButtonSize.small,
              onPressed: _isExtracting ? null : _removeBackSide,
            ),
        ],
      ),
    );
  }

  Widget _confidenceNote(String label, TextEditingController controller, double? confidence) {
    if (controller.text.trim().isEmpty || confidence == null || confidence >= 0.75) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 2, bottom: 2),
      child: Text(
        '$label confidence: ${(confidence * 100).toInt()}% - please check',
        style: MlTypography.caption.copyWith(color: const Color(0xFFB45309), fontWeight: FontWeight.w600),
      ),
    );
  }

  void _moveNameToAddress() {
    setState(() {
      final moved = _nameController.text.trim();
      final current = _addressController.text.trim();
      _addressController.text = current.isEmpty ? moved : '$current, $moved';
      _nameController.clear();
      _nameConfidence = null;
      _addressConfidence = 0.6;
      _refreshReviewGate();
    });
  }

  Widget _nameAddressWarning() {
    if (!_nameIsLikelyAddress) return const SizedBox.shrink();
    return Container(
      margin: const EdgeInsets.only(top: MlSpacing.xs),
      padding: const EdgeInsets.all(MlSpacing.xs),
      decoration: BoxDecoration(
        color: const Color(0xFFFEF2F2),
        borderRadius: BorderRadius.circular(MlRadii.sm),
        border: Border.all(color: const Color(0xFFEF4444)),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              'This looks like an address, not a person\'s name.',
              style: MlTypography.caption.copyWith(color: const Color(0xFF991B1B), fontWeight: FontWeight.w600),
            ),
          ),
          MlButton(
            label: 'Move to Address',
            variant: MlButtonVariant.primary,
            size: MlButtonSize.small,
            onPressed: _moveNameToAddress,
          ),
        ],
      ),
    );
  }

  void _chooseConflict(CardFieldConflictDto conflict, String value) {
    setState(() {
      switch (conflict.field) {
        case 'Company':
          _companyController.text = value;
        case 'Name':
          _nameController.text = value;
        case 'Job title':
          _titleController.text = value;
        case 'Email':
          _emailController.text = value;
        case 'Phone':
          _phoneController.text = value;
      }
      _conflicts = _conflicts.where((c) => c.field != conflict.field).toList();
      _refreshReviewGate();
    });
  }

  Widget _conflictChip(CardFieldConflictDto conflict, String side, String value) {
    return GestureDetector(
      onTap: () => _chooseConflict(conflict, value),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        decoration: BoxDecoration(
          color: MlColors.surfaceElevated,
          borderRadius: BorderRadius.circular(MlRadii.sm),
          border: Border.all(color: MlColors.primary.withOpacity(0.5)),
        ),
        child: Text('$side: $value', style: MlTypography.caption.copyWith(color: MlColors.primary)),
      ),
    );
  }

  Widget _conflictPanel() {
    if (_conflicts.isEmpty) return const SizedBox.shrink();
    return Container(
      margin: const EdgeInsets.only(bottom: MlSpacing.sm),
      padding: const EdgeInsets.all(MlSpacing.xs),
      decoration: BoxDecoration(
        color: const Color(0xFFFEF3C7),
        borderRadius: BorderRadius.circular(MlRadii.sm),
        border: Border.all(color: const Color(0xFFF59E0B)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Front and back show different values. Tap the correct one.',
            style: MlTypography.caption.copyWith(color: const Color(0xFF92400E), fontWeight: FontWeight.w700),
          ),
          for (final c in _conflicts) ...[
            const SizedBox(height: MlSpacing.xs),
            Text(c.field, style: MlTypography.caption.copyWith(color: const Color(0xFF78350F))),
            const SizedBox(height: 2),
            Wrap(
              spacing: MlSpacing.xs,
              runSpacing: MlSpacing.xs,
              children: [
                _conflictChip(c, 'Front', c.frontValue),
                _conflictChip(c, 'Back', c.backValue),
              ],
            ),
          ],
        ],
      ),
    );
  }

  /// The approval prompt for a card held for review. The evidence overlay carries its own copy when it is open.
  Widget _reviewBanner() {
    if (!_requiresReview || (_showReviewMode && _frontEvidence.isNotEmpty)) return const SizedBox.shrink();
    return Container(
      padding: const EdgeInsets.all(MlSpacing.xs),
      decoration: BoxDecoration(
        color: _isReviewConfirmed ? const Color(0xFFECFDF5) : const Color(0xFFFEF2F2),
        borderRadius: BorderRadius.circular(MlRadii.sm),
        border: Border.all(color: _isReviewConfirmed ? const Color(0xFF10B981) : const Color(0xFFEF4444)),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              _isReviewConfirmed ? '✓ Checked and approved.' : '⚠️ Check before saving: ${_lowFields.join(', ')}.',
              style: MlTypography.caption.copyWith(
                color: _isReviewConfirmed ? const Color(0xFF065F46) : const Color(0xFF991B1B),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          MlButton(
            label: _isReviewConfirmed ? 'Approved' : 'Approve',
            variant: _isReviewConfirmed ? MlButtonVariant.ghost : MlButtonVariant.primary,
            size: MlButtonSize.small,
            onPressed: () => setState(() => _isReviewConfirmed = true),
          ),
        ],
      ),
    );
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
            'Capture the front of the business card, and the back if it has details. Contact, company and address are extracted automatically.',
            style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted),
          ),
          const SizedBox(height: MlSpacing.md),

          // Front and back capture
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _sideSlot(isBack: false),
              const SizedBox(width: MlSpacing.md),
              _sideSlot(isBack: true),
            ],
          ),
          const SizedBox(height: MlSpacing.md),

          // Captured Photo Preview & Review OCR Mode
          if (_imageBytes != null) ...[
            Container(
              padding: const EdgeInsets.all(MlSpacing.sm),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.md),
                border: Border.all(color: MlColors.primary.withOpacity(0.4)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      ClipRRect(
                        borderRadius: BorderRadius.circular(MlRadii.sm),
                        child: Image.memory(
                          _imageBytes!,
                          width: 54,
                          height: 54,
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
                              '${(_imageBytes!.lengthInBytes / 1024).toStringAsFixed(1)} KB • ${_frontEvidence.length} words',
                              style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                            ),
                          ],
                        ),
                      ),
                      if (_frontEvidence.isNotEmpty)
                        MlButton(
                          label: _showReviewMode ? 'Hide Review' : '🔍 Review OCR',
                          variant: _requiresReview && !_isReviewConfirmed
                              ? MlButtonVariant.secondary
                              : MlButtonVariant.ghost,
                          size: MlButtonSize.small,
                          onPressed: () => setState(() => _showReviewMode = !_showReviewMode),
                        )
                      else
                        MlButton(
                          label: 'Re-scan',
                          variant: MlButtonVariant.ghost,
                          size: MlButtonSize.small,
                          onPressed: () => _scanSides(),
                        ),
                    ],
                  ),

                  // Review OCR Bounding Box Overlay
                  if (_showReviewMode && _frontEvidence.isNotEmpty) ...[
                    const SizedBox(height: MlSpacing.sm),
                    Container(
                      padding: const EdgeInsets.all(MlSpacing.xs),
                      decoration: BoxDecoration(
                        color: MlColors.surfaceElevated,
                        borderRadius: BorderRadius.circular(MlRadii.sm),
                        border: Border.all(color: MlColors.border),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              Text(
                                'Card Evidence & Bounding Boxes',
                                style: MlTypography.caption.copyWith(
                                  fontWeight: FontWeight.w700,
                                  color: MlColors.textPrimary,
                                ),
                              ),
                              Text(
                                '${_frontEvidence.where((b) => b.confidence < 0.75).length} low confidence',
                                style: MlTypography.caption.copyWith(
                                  color: _frontEvidence.any((b) => b.confidence < 0.75)
                                      ? const Color(0xFFDC2626)
                                      : const Color(0xFF059669),
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: MlSpacing.xs),
                          LayoutBuilder(
                            builder: (context, constraints) {
                              final width = constraints.maxWidth;
                              final height = width / 1.75;
                              return ClipRRect(
                                borderRadius: BorderRadius.circular(MlRadii.sm),
                                child: Container(
                                  width: width,
                                  height: height,
                                  color: const Color(0xFF0F172A),
                                  child: Stack(
                                    children: [
                                      Positioned.fill(
                                        child: Image.memory(
                                          _imageBytes!,
                                          fit: BoxFit.contain,
                                        ),
                                      ),
                                      ..._frontEvidence.map((box) {
                                        final isLow = box.confidence < 0.75;
                                        final boxLeft = (box.x * width).clamp(0.0, width - 10.0);
                                        final boxTop = (box.y * height).clamp(0.0, height - 10.0);
                                        final boxWidth = (box.width * width).clamp(16.0, width - boxLeft);
                                        final boxHeight = (box.height * height).clamp(10.0, height - boxTop);

                                        return Positioned(
                                          left: boxLeft,
                                          top: boxTop,
                                          width: boxWidth,
                                          height: boxHeight,
                                          child: GestureDetector(
                                            onTap: () {
                                              MlToast.show(
                                                context,
                                                message: '"${box.text}" (Conf: ${(box.confidence * 100).toInt()}%)',
                                                type: isLow ? MlToastType.error : MlToastType.info,
                                              );
                                            },
                                            child: Container(
                                              decoration: BoxDecoration(
                                                color: isLow
                                                    ? const Color(0x35EF4444)
                                                    : const Color(0x2510B981),
                                                border: Border.all(
                                                  color: isLow
                                                      ? const Color(0xFFEF4444)
                                                      : const Color(0xFF10B981),
                                                  width: isLow ? 1.8 : 1.0,
                                                ),
                                                borderRadius: BorderRadius.circular(2),
                                              ),
                                              child: isLow
                                                  ? Align(
                                                      alignment: Alignment.topLeft,
                                                      child: Container(
                                                        padding: const EdgeInsets.symmetric(horizontal: 1.5, vertical: 0.5),
                                                        color: const Color(0xFFEF4444),
                                                        child: Text(
                                                          '${(box.confidence * 100).toInt()}%',
                                                          style: const TextStyle(
                                                            fontSize: 7,
                                                            color: Color(0xFFFFFFFF),
                                                            fontWeight: FontWeight.bold,
                                                          ),
                                                        ),
                                                      ),
                                                    )
                                                  : null,
                                            ),
                                          ),
                                        );
                                      }),
                                    ],
                                  ),
                                ),
                              );
                            },
                          ),
                          const SizedBox(height: MlSpacing.xs),
                          Row(
                            children: [
                              Container(
                                width: 8,
                                height: 8,
                                decoration: BoxDecoration(
                                  color: const Color(0xFF10B981),
                                  borderRadius: BorderRadius.circular(2),
                                ),
                              ),
                              const SizedBox(width: 4),
                              Text('High (≥75%)', style: MlTypography.caption.copyWith(color: MlColors.textMuted)),
                              const SizedBox(width: MlSpacing.md),
                              Container(
                                width: 8,
                                height: 8,
                                decoration: BoxDecoration(
                                  color: const Color(0xFFEF4444),
                                  borderRadius: BorderRadius.circular(2),
                                ),
                              ),
                              const SizedBox(width: 4),
                              Text(
                                'Low (<75%) • Tap to inspect',
                                style: MlTypography.caption.copyWith(color: const Color(0xFFDC2626)),
                              ),
                            ],
                          ),
                          if (_requiresReview) ...[
                            const SizedBox(height: MlSpacing.xs),
                            Container(
                              padding: const EdgeInsets.all(MlSpacing.xs),
                              decoration: BoxDecoration(
                                color: _isReviewConfirmed ? const Color(0xFFECFDF5) : const Color(0xFFFEF2F2),
                                borderRadius: BorderRadius.circular(MlRadii.sm),
                                border: Border.all(
                                  color: _isReviewConfirmed ? const Color(0xFF10B981) : const Color(0xFFEF4444),
                                ),
                              ),
                              child: Row(
                                children: [
                                  Expanded(
                                    child: Text(
                                      _isReviewConfirmed
                                          ? '✓ OCR review verified and approved by user.'
                                          : '⚠️ Check before saving: ${_lowFields.join(', ')}.',
                                      style: MlTypography.caption.copyWith(
                                        color: _isReviewConfirmed ? const Color(0xFF065F46) : const Color(0xFF991B1B),
                                        fontWeight: FontWeight.w600,
                                      ),
                                    ),
                                  ),
                                  MlButton(
                                    label: _isReviewConfirmed ? 'Approved' : 'Approve OCR',
                                    variant: _isReviewConfirmed ? MlButtonVariant.ghost : MlButtonVariant.primary,
                                    size: MlButtonSize.small,
                                    onPressed: () => setState(() => _isReviewConfirmed = true),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ],
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
                          _engineConfidence != null
                              ? '$_detectedScript • OCR: ${((_engineConfidence!) * 100).toInt()}% • Extraction: ${((_overallConfidence ?? 0.8) * 100).toInt()}%'
                              : '$_detectedScript • Extraction Confidence: ${((_overallConfidence ?? 0.8) * 100).toInt()}%',
                          style: MlTypography.caption.copyWith(color: MlColors.primary, fontWeight: FontWeight.w600),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.sm),
                  _conflictPanel(),
                  MlTextField(
                    label: 'Company Name *',
                    controller: _companyController,
                  ),
                  _confidenceNote('Company', _companyController, _companyConfidence),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Contact Person *',
                    controller: _nameController,
                    onChanged: (_) => setState(_refreshReviewGate),
                  ),
                  _confidenceNote('Name', _nameController, _nameConfidence),
                  _nameAddressWarning(),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Phone Number',
                    controller: _phoneController,
                  ),
                  _confidenceNote('Phone', _phoneController, _phoneConfidence),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Email Address',
                    controller: _emailController,
                  ),
                  _confidenceNote('Email', _emailController, _emailConfidence),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Job Title',
                    controller: _titleController,
                  ),
                  _confidenceNote('Job title', _titleController, _titleConfidence),
                  const SizedBox(height: MlSpacing.xs),
                  MlTextField(
                    label: 'Address',
                    controller: _addressController,
                    minLines: 2,
                    maxLines: 4,
                    inputFormatters: [LengthLimitingTextInputFormatter(500)],
                    helperText: 'Up to 500 characters',
                    onChanged: (_) => setState(_refreshReviewGate),
                  ),
                  _confidenceNote('Address', _addressController, _addressConfidence),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            _reviewBanner(),
            const SizedBox(height: MlSpacing.sm),

            MlButton(
              label: (_requiresReview && !_isReviewConfirmed)
                  ? '⚠️ Review & Approval Required to Save'
                  : (_duplicateCandidate != null ? 'Create Lead Anyway (Duplicate Warning)' : 'Save Lead to CRM'),
              variant: (_requiresReview && !_isReviewConfirmed)
                  ? MlButtonVariant.secondary
                  : (_duplicateCandidate != null ? MlButtonVariant.secondary : MlButtonVariant.primary),
              isLoading: _isSaving,
              onPressed: _isSaving ? null : _handleSaveLead,
            ),
          ],
        ],
      ),
    );
  }
}
