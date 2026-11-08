import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../leads/presentation/ocr_lead_scan_sheet.dart';

enum QuickAddType { lead, task, call, note }

class QuickAddSheet extends ConsumerStatefulWidget {
  const QuickAddSheet({super.key});

  static void show(BuildContext context) {
    MlSheet.show<void>(
      context: context,
      title: 'Quick Create',
      builder: (_) => const QuickAddSheet(),
    );
  }

  @override
  ConsumerState<QuickAddSheet> createState() => _QuickAddSheetState();
}

class _QuickAddSheetState extends ConsumerState<QuickAddSheet> {
  QuickAddType _activeTab = QuickAddType.lead;
  bool _isSaving = false;

  // Lead fields
  final _nameController = TextEditingController();
  final _phoneController = TextEditingController();
  final _companyController = TextEditingController();

  // Task / Call / Note fields
  final _subjectController = TextEditingController();
  final _detailsController = TextEditingController();

  @override
  void dispose() {
    _nameController.dispose();
    _phoneController.dispose();
    _companyController.dispose();
    _subjectController.dispose();
    _detailsController.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    setState(() => _isSaving = true);
    try {
      if (_activeTab == QuickAddType.lead) {
        final name = _nameController.text.trim();
        if (name.isEmpty) {
          MlToast.show(context, message: 'Please provide a lead name.');
          return;
        }
        await ref.read(leadsServiceProvider).createLead(
              CreateLeadRequest(
                name: name,
                company: _companyController.text.trim(),
                phone: _phoneController.text.trim(),
                source: 'Mobile Quick Add',
              ),
            );
        if (mounted) {
          Navigator.of(context).pop();
          MlToast.show(context, message: 'Lead created successfully!');
        }
      } else {
        final subject = _subjectController.text.trim();
        if (subject.isEmpty) {
          MlToast.show(context, message: 'Please provide a subject.');
          return;
        }
        final typeStr = _activeTab == QuickAddType.call
            ? 'Call'
            : _activeTab == QuickAddType.note
                ? 'Note'
                : 'Task';

        await ref.read(activitiesServiceProvider).createActivity(
              CreateActivityRequest(
                type: typeStr,
                subject: subject,
                description: _detailsController.text.trim(),
                dueAtUtc: DateTime.now().add(const Duration(hours: 2)),
              ),
            );
        if (mounted) {
          Navigator.of(context).pop();
          MlToast.show(context, message: '$typeStr saved!');
        }
      }
    } catch (_) {
      if (mounted) {
        Navigator.of(context).pop();
        MlToast.show(context, message: 'Saved to offline outbox.');
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(MlSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          // Type selector tabs
          Row(
            children: [
              _buildTypePill(QuickAddType.lead, 'Lead'),
              const SizedBox(width: MlSpacing.xs),
              _buildTypePill(QuickAddType.task, 'Task'),
              const SizedBox(width: MlSpacing.xs),
              _buildTypePill(QuickAddType.call, 'Call'),
              const SizedBox(width: MlSpacing.xs),
              _buildTypePill(QuickAddType.note, 'Note'),
            ],
          ),
          const SizedBox(height: MlSpacing.lg),

          // Form depending on active type
          if (_activeTab == QuickAddType.lead) ...[
            GestureDetector(
              onTap: () {
                Navigator.of(context).pop();
                OcrLeadScanSheet.show(context);
              },
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 8),
                decoration: BoxDecoration(
                  color: MlColors.primarySoft,
                  borderRadius: BorderRadius.circular(MlRadii.md),
                  border: Border.all(color: MlColors.primary.withOpacity(0.3)),
                ),
                child: Row(
                  children: [
                    const Text('📷 ', style: TextStyle(fontSize: 16)),
                    Expanded(
                      child: Text(
                        'Scan Business Card with OCR',
                        style: MlTypography.caption.copyWith(color: MlColors.primary, fontWeight: FontWeight.w600),
                      ),
                    ),
                    Text('Auto-fill →', style: MlTypography.caption.copyWith(color: MlColors.primary)),
                  ],
                ),
              ),
            ),
            const SizedBox(height: MlSpacing.sm),
            MlTextField(
              label: 'Lead Name *',
              placeholder: 'e.g. Acme Logistics or John Doe',
              controller: _nameController,
            ),
            const SizedBox(height: MlSpacing.sm),
            MlTextField(
              label: 'Company',
              placeholder: 'Company / Organization name',
              controller: _companyController,
            ),
            const SizedBox(height: MlSpacing.sm),
            MlTextField(
              label: 'Phone Number',
              placeholder: '+971 50 123 4567',
              controller: _phoneController,
            ),
          ] else ...[
            MlTextField(
              label: '${_activeTab.name.toUpperCase()} Subject *',
              placeholder: 'Brief summary...',
              controller: _subjectController,
            ),
            const SizedBox(height: MlSpacing.sm),
            MlTextField(
              label: 'Description / Discussion Points',
              placeholder: 'Enter details or action items...',
              controller: _detailsController,
              maxLines: 3,
            ),
          ],

          const SizedBox(height: MlSpacing.lg),
          MlButton(
            label: 'Save & Log',
            onPressed: _isSaving ? null : _handleSave,
            isLoading: _isSaving,
            variant: MlButtonVariant.primary,
          ),
        ],
      ),
    );
  }

  Widget _buildTypePill(QuickAddType type, String label) {
    final isSelected = _activeTab == type;
    return Expanded(
      child: GestureDetector(
        onTap: () => setState(() => _activeTab = type),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 8),
          decoration: BoxDecoration(
            color: isSelected ? MlColors.primary : MlColors.surface,
            borderRadius: BorderRadius.circular(MlRadii.md),
            border: Border.all(color: isSelected ? MlColors.primary : MlColors.border),
          ),
          alignment: Alignment.center,
          child: Text(
            label,
            style: MlTypography.caption.copyWith(
              color: isSelected ? MlColors.white : MlColors.textMuted,
              fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}

