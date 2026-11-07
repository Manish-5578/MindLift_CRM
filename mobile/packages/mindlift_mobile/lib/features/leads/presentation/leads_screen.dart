import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class LeadsScreen extends ConsumerStatefulWidget {
  const LeadsScreen({super.key});

  @override
  ConsumerState<LeadsScreen> createState() => _LeadsScreenState();
}

class _LeadsScreenState extends ConsumerState<LeadsScreen> {
  final _searchController = TextEditingController();
  String _selectedStatus = 'All';
  bool _isLoading = false;
  List<LeadDto> _leads = [];

  static const _statusFilters = ['All', 'New', 'Contacted', 'Qualified', 'Lost'];

  @override
  void initState() {
    super.initState();
    _fetchLeads();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _fetchLeads() async {
    setState(() => _isLoading = true);
    try {
      final req = LeadQueryRequest(
        searchTerm: _searchController.text.trim(),
        status: _selectedStatus == 'All' ? null : _selectedStatus,
      );
      final results = await ref.read(leadsServiceProvider).queryLeads(req);
      if (mounted) setState(() => _leads = results);
    } catch (_) {
      // In offline mode, keep existing or cached leads
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return MlScreen(
      title: 'Leads',
      subtitle: '${_leads.length} Active Records',
      actions: [
        MlIconButton(
          label: 'Refresh Leads',
          icon: const Text('↻', style: TextStyle(fontSize: 20, color: MlColors.textPrimary)),
          onPressed: _fetchLeads,
        ),
      ],
      body: Column(
        children: [
          // Search & Filter header
          Container(
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: MlSpacing.sm),
            child: Column(
              children: [
                MlSearchField(
                  placeholder: 'Search name, phone, company...',
                  controller: _searchController,
                  onChanged: (_) => _fetchLeads(),
                  onClear: () {
                    _searchController.clear();
                    _fetchLeads();
                  },
                ),
                const SizedBox(height: MlSpacing.sm),
                // Filter chips row
                SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: Row(
                    children: _statusFilters.map((s) {
                      final isSelected = s == _selectedStatus;
                      return Padding(
                        padding: const EdgeInsets.only(right: MlSpacing.xs),
                        child: GestureDetector(
                          onTap: () {
                            setState(() => _selectedStatus = s);
                            _fetchLeads();
                          },
                          child: Container(
                            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                            decoration: BoxDecoration(
                              color: isSelected ? MlColors.primary : MlColors.surface,
                              borderRadius: BorderRadius.circular(MlRadii.full),
                              border: Border.all(
                                color: isSelected ? MlColors.primary : MlColors.border,
                              ),
                            ),
                            child: Text(
                              s,
                              style: MlTypography.caption.copyWith(
                                color: isSelected ? MlColors.white : MlColors.textMuted,
                                fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
                              ),
                            ),
                          ),
                        ),
                      );
                    }).toList(),
                  ),
                ),
              ],
            ),
          ),

          // Leads List
          Expanded(
            child: MlPullToRefresh(
              onRefresh: _fetchLeads,
              child: _isLoading && _leads.isEmpty
                  ? ListView.builder(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      itemCount: 4,
                      itemBuilder: (_, __) => const Padding(
                        padding: EdgeInsets.only(bottom: MlSpacing.sm),
                        child: MlSkeleton(height: 76),
                      ),
                    )
                  : _leads.isEmpty
                      ? MlEmptyState(
                          title: 'No leads found',
                          subtitle: 'Try adjusting your search query or filters.',
                          actionLabel: 'Reset Filters',
                          onAction: () {
                            _searchController.clear();
                            setState(() => _selectedStatus = 'All');
                            _fetchLeads();
                          },
                        )
                      : ListView.builder(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(MlSpacing.md),
                          itemCount: _leads.length,
                          itemBuilder: (context, index) {
                            final lead = _leads[index];
                            return Padding(
                              padding: const EdgeInsets.only(bottom: MlSpacing.sm),
                              child: MlRecordCard(
                                leading: MlAvatar(name: lead.name, size: MlAvatarSize.md),
                                title: lead.name,
                                subtitle: '${lead.company ?? 'Individual'} • ${lead.phone ?? 'No phone'}',
                                statusChip: MlStatusChip(
                                  label: lead.status,
                                  state: lead.status == 'Qualified'
                                      ? MlStatusState.success
                                      : lead.status == 'Lost'
                                          ? MlStatusState.danger
                                          : MlStatusState.primary,
                                ),
                                metadata: [
                                  if (lead.source != null)
                                    Container(
                                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                                      decoration: BoxDecoration(
                                        color: MlColors.surfaceLight,
                                        borderRadius: BorderRadius.circular(MlRadii.sm),
                                      ),
                                      child: Text(
                                        lead.source!,
                                        style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                                      ),
                                    ),
                                  if (lead.notesCount > 0)
                                    Text(
                                      '💬 ${lead.notesCount} notes',
                                      style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                                    ),
                                ],
                                onTap: () => context.push('/leads/${lead.id}'),
                              ),
                            );
                          },
                        ),
            ),
          ),
        ],
      ),
    );
  }
}
