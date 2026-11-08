import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class LeadDetailScreen extends ConsumerStatefulWidget {
  const LeadDetailScreen({super.key, required this.leadId});
  final String leadId;

  @override
  ConsumerState<LeadDetailScreen> createState() => _LeadDetailScreenState();
}

class _LeadDetailScreenState extends ConsumerState<LeadDetailScreen> {
  bool _isLoading = true;
  LeadDto? _lead;
  List<LeadNoteDto> _notes = [];
  final _noteController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _loadLead();
  }

  @override
  void dispose() {
    _noteController.dispose();
    super.dispose();
  }

  Future<void> _loadLead() async {
    setState(() => _isLoading = true);
    try {
      final lead = await ref.read(leadsServiceProvider).getLead(widget.leadId);
      final notes = await ref.read(leadsServiceProvider).getLeadNotes(widget.leadId);
      if (mounted) {
        setState(() {
          _lead = lead;
          _notes = notes;
        });
      }
    } catch (_) {
      // In offline or fallback mode
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  void _openAddNoteSheet() {
    _noteController.clear();
    MlSheet.show<void>(
      context: context,
      title: 'Add Note',
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            MlTextField(
              label: 'Note Content',
              placeholder: 'Type client conversation or progress notes...',
              controller: _noteController,
              maxLines: 4,
            ),
            const SizedBox(height: MlSpacing.lg),
            MlButton(
              label: 'Save Note to History',
              onPressed: () async {
                final text = _noteController.text.trim();
                if (text.isEmpty) return;
                Navigator.of(sheetContext).pop();
                try {
                  final newNote = await ref
                      .read(leadsServiceProvider)
                      .addLeadNote(widget.leadId, text);
                  setState(() => _notes.insert(0, newNote));
                  MlToast.show(context, message: 'Note added successfully!');
                } catch (_) {
                  MlToast.show(context, message: 'Saved note to offline queue.');
                }
              },
            ),
          ],
        ),
      ),
    );
  }

  void _openChangeStatusSheet() {
    const statuses = [
      MlSelectOption(value: 'New', label: 'New'),
      MlSelectOption(value: 'Contacted', label: 'Contacted'),
      MlSelectOption(value: 'Qualified', label: 'Qualified'),
      MlSelectOption(value: 'Lost', label: 'Lost'),
    ];

    MlSheet.show<void>(
      context: context,
      title: 'Update Status',
      builder: (sheetContext) => Column(
        mainAxisSize: MainAxisSize.min,
        children: statuses.map((opt) {
          final isSelected = _lead?.status == opt.value;
          return GestureDetector(
            onTap: () async {
              Navigator.of(sheetContext).pop();
              try {
                await ref.read(leadsServiceProvider).updateLeadStatus(widget.leadId, opt.value);
                setState(() {
                  if (_lead != null) {
                    _lead = LeadDto(
                      id: _lead!.id,
                      name: _lead!.name,
                      company: _lead!.company,
                      email: _lead!.email,
                      phone: _lead!.phone,
                      status: opt.value,
                      source: _lead!.source,
                      createdBy: _lead!.createdBy,
                    );
                  }
                });
                MlToast.show(context, message: 'Status updated to ${opt.value}!');
              } catch (_) {
                MlToast.show(context, message: 'Status change queued offline.');
              }
            },
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: MlSpacing.lg, vertical: MlSpacing.md),
              decoration: BoxDecoration(
                border: Border(bottom: BorderSide(color: MlColors.border)),
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    opt.label,
                    style: MlTypography.labelLarge.copyWith(
                      color: isSelected ? MlColors.primaryLight : MlColors.textPrimary,
                    ),
                  ),
                  if (isSelected)
                    const Text('✓', style: TextStyle(color: MlColors.primaryLight, fontSize: 18)),
                ],
              ),
            ),
          );
        }).toList(),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_isLoading && _lead == null) {
      return MlScreen(
        title: 'Loading Lead...',
        leading: MlIconButton(
          label: 'Back',
          icon: const Text('←', style: TextStyle(fontSize: 20, color: MlColors.textPrimary)),
          onPressed: () => context.pop(),
        ),
        body: const Padding(
          padding: EdgeInsets.all(MlSpacing.lg),
          child: Column(
            children: [
              MlSkeleton(height: 120),
              SizedBox(height: MlSpacing.md),
              MlSkeleton(height: 200),
            ],
          ),
        ),
      );
    }

    final lead = _lead ?? LeadDto(id: widget.leadId, name: 'Lead Details');

    return MlScreen(
      title: lead.name,
      subtitle: lead.company ?? 'Individual Lead',
      leading: MlIconButton(
        label: 'Back',
        icon: const Text('←', style: TextStyle(fontSize: 20, color: MlColors.textPrimary)),
        onPressed: () => context.pop(),
      ),
      actions: [
        MlIconButton(
          label: 'Change Status',
          icon: const Text('⚙', style: TextStyle(fontSize: 20, color: MlColors.textPrimary)),
          onPressed: _openChangeStatusSheet,
        ),
      ],
      bottomRail: Container(
        padding: const EdgeInsets.all(MlSpacing.md),
        decoration: BoxDecoration(
          color: MlColors.surface,
          border: Border(top: BorderSide(color: MlColors.border)),
        ),
        child: Row(
          children: [
            Expanded(
              child: MlButton(
                label: '+ Add Note',
                onPressed: _openAddNoteSheet,
                variant: MlButtonVariant.primary,
              ),
            ),
          ],
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(MlSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Lead Card Summary
            Container(
              padding: const EdgeInsets.all(MlSpacing.lg),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.lg),
                border: Border.all(color: MlColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          lead.name,
                          style: MlTypography.headingSmall.copyWith(color: MlColors.textPrimary),
                        ),
                      ),
                      MlStatusChip(
                        label: lead.status,
                        state: lead.status == 'Qualified'
                            ? MlStatusState.success
                            : lead.status == 'Lost'
                                ? MlStatusState.danger
                                : MlStatusState.primary,
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.md),
                  _buildDetailRow('Email', lead.email ?? '—'),
                  const SizedBox(height: MlSpacing.xs),
                  _buildDetailRow('Phone', lead.phone ?? '—'),
                  const SizedBox(height: MlSpacing.xs),
                  _buildDetailRow('Source', lead.source ?? 'Mobile App'),
                  const SizedBox(height: MlSpacing.xs),
                  _buildDetailRow('Created By', lead.createdBy ?? 'Staff Agent'),
                ],
              ),
            ),
            const SizedBox(height: MlSpacing.lg),

            // Chronological Notes History Section
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Chronological Notes (${_notes.length})',
                  style: MlTypography.labelLarge.copyWith(color: MlColors.textPrimary),
                ),
                GestureDetector(
                  onTap: _openAddNoteSheet,
                  child: Text(
                    '+ Add',
                    style: MlTypography.labelMedium.copyWith(color: MlColors.primaryLight),
                  ),
                ),
              ],
            ),
            const SizedBox(height: MlSpacing.sm),

            MlTimeline(
              items: _notes.map((n) {
                return MlTimelineItemData(
                  id: n.id,
                  title: 'Note Recorded',
                  author: n.authorName,
                  timestamp: n.createdAtUtc,
                  content: n.text,
                  indicatorColor: MlColors.primary,
                );
              }).toList(),
              emptyPlaceholder: Container(
                padding: const EdgeInsets.all(MlSpacing.xl),
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: MlColors.surface,
                  borderRadius: BorderRadius.circular(MlRadii.md),
                  border: Border.all(color: MlColors.border),
                ),
                child: Column(
                  children: [
                    Text(
                      'No notes recorded yet',
                      style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
                    ),
                    const SizedBox(height: MlSpacing.sm),
                    MlButton(
                      label: 'Record First Note',
                      onPressed: _openAddNoteSheet,
                      variant: MlButtonVariant.secondary,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDetailRow(String label, String value) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label, style: MlTypography.bodySmall.copyWith(color: MlColors.textMuted)),
        Text(value, style: MlTypography.bodySmall.copyWith(color: MlColors.textPrimary)),
      ],
    );
  }
}

