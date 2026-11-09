import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../../core/offline_store.dart';
import '../../leads/presentation/lead_actions.dart';
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
        final req = CreateLeadRequest(
          name: name,
          company: _companyController.text.trim(),
          phone: _phoneController.text.trim(),
          source: 'Mobile Quick Add',
        );
        final sent = await sendOrQueue(
          ref,
          send: () => ref.read(leadsServiceProvider).createLead(req),
          method: 'POST',
          path: '/api/v1/leads',
          body: req.toJson(),
          label: 'Create lead $name',
        );
        if (mounted) {
          Navigator.of(context).pop();
          MlToast.show(context, message: sent ? 'Lead created successfully!' : 'Saved on this phone. It will sync when you are back online.');
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

        final req = CreateActivityRequest(
          type: typeStr,
          subject: subject,
          description: _detailsController.text.trim(),
          dueAtUtc: DateTime.now().add(const Duration(hours: 2)),
        );
        final sent = await sendOrQueue(
          ref,
          send: () => ref.read(activitiesServiceProvider).createActivity(req),
          method: 'POST',
          path: '/api/v1/activities',
          body: req.toJson(),
          label: 'Create $typeStr $subject',
        );
        if (mounted) {
          Navigator.of(context).pop();
          MlToast.show(context, message: sent ? '$typeStr saved!' : 'Saved on this phone. It will sync when you are back online.');
        }
      }
    } catch (e) {
      // Keep the sheet open so nothing typed is lost, and say what actually went wrong.
      if (mounted) {
        MlToast.show(context, message: LeadActions.errorText(e, 'Could not save. Please try again.'), type: MlToastType.error);
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
                padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: 12),
                decoration: BoxDecoration(
                  color: MlColors.primarySoft,
                  borderRadius: BorderRadius.circular(MlRadii.md),
                  border: Border.all(color: MlColors.primary.withOpacity(0.3)),
                ),
                child: Row(
                  children: [
                    MlIcon(MlIcons.scan, size: 22, color: MlColors.primary),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        'Scan Business Card with OCR',
                        style: MlTypography.caption.copyWith(color: MlColors.primaryText, fontWeight: FontWeight.w600),
                      ),
                    ),
                    MlIcon(MlIcons.chevronRight, size: 20, color: MlColors.primary),
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

  static const _tabIcons = {
    QuickAddType.lead: MlIcons.leadsOutline,
    QuickAddType.task: MlIcons.checkCircle,
    QuickAddType.call: MlIcons.call,
    QuickAddType.note: MlIcons.note,
  };

  Widget _buildTypePill(QuickAddType type, String label) {
    final isSelected = _activeTab == type;
    final fg = isSelected ? MlColors.white : MlColors.textSecondary;
    return Expanded(
      child: MlPressable(
        onTap: () => setState(() => _activeTab = type),
        scale: 0.94,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOutCubic,
          padding: const EdgeInsets.symmetric(vertical: 10),
          decoration: BoxDecoration(
            color: isSelected ? MlColors.primary : MlColors.surfaceElevated,
            borderRadius: BorderRadius.circular(MlRadii.lg),
          ),
          alignment: Alignment.center,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              MlIcon(_tabIcons[type]!, size: 22, color: fg),
              const SizedBox(height: 3),
              Text(label,
                  style: TextStyle(fontSize: 12.5, color: fg, fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500, decoration: TextDecoration.none)),
            ],
          ),
        ),
      ),
    );
  }
}
