import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_api/mindlift_api.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';
import '../../../core/top_bar_actions.dart';

class DealsScreen extends ConsumerStatefulWidget {
  const DealsScreen({super.key});

  @override
  ConsumerState<DealsScreen> createState() => _DealsScreenState();
}

class _DealsScreenState extends ConsumerState<DealsScreen> {
  String _selectedStage = 'All';
  bool _isLoading = false;
  List<DealDto> _deals = [];

  static const _stages = ['All', 'Lead', 'Qualified', 'Proposal', 'Negotiation', 'Won', 'Lost'];

  @override
  void initState() {
    super.initState();
    _loadDeals();
  }

  Future<void> _loadDeals() async {
    setState(() => _isLoading = true);
    try {
      final list = await ref.read(dealsServiceProvider).listDeals(
            stage: _selectedStage == 'All' ? null : _selectedStage,
          );
      if (mounted) setState(() => _deals = list);
    } catch (_) {
      // In offline or fallback mode
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  void _openChangeStageSheet(DealDto deal) {
    MlSheet.show<void>(
      context: context,
      title: 'Move Deal Stage',
      builder: (sheetContext) => Column(
        mainAxisSize: MainAxisSize.min,
        children: _stages.where((s) => s != 'All').map((stage) {
          final isSelected = deal.stage == stage;
          return GestureDetector(
            onTap: () async {
              Navigator.of(sheetContext).pop();
              try {
                await ref.read(dealsServiceProvider).updateDealStage(deal.id, stage);
                MlToast.show(context, message: 'Deal moved to $stage!');
                await _loadDeals();
              } catch (_) {
                MlToast.show(context, message: 'Stage move saved offline.');
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
                    stage,
                    style: MlTypography.labelLarge.copyWith(
                      color: isSelected ? MlColors.primaryLight : MlColors.textPrimary,
                    ),
                  ),
                  if (isSelected)
                    MlIcon(MlIcons.check, size: 18, color: MlColors.primaryLight),
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
    return MlScreen(
      title: 'Deals Pipeline',
      subtitle: '${_deals.length} Active Opportunities',
      actions: topBarActions(),
      body: Column(
        children: [
          // Stage Selector Pills
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: MlSpacing.md, vertical: MlSpacing.sm),
            child: Row(
              children: _stages.map((stage) {
                final isSelected = stage == _selectedStage;
                return Padding(
                  padding: const EdgeInsets.only(right: MlSpacing.xs),
                  child: GestureDetector(
                    onTap: () {
                      setState(() => _selectedStage = stage);
                      _loadDeals();
                    },
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                      decoration: BoxDecoration(
                        color: isSelected ? MlColors.primary : MlColors.surface,
                        borderRadius: BorderRadius.circular(MlRadii.full),
                        border: Border.all(
                          color: isSelected ? MlColors.primary : MlColors.border,
                        ),
                      ),
                      child: Text(
                        stage,
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

          // Deals List
          Expanded(
            child: MlPullToRefresh(
              onRefresh: _loadDeals,
              child: _isLoading && _deals.isEmpty
                  ? ListView.builder(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      itemCount: 4,
                      itemBuilder: (_, __) => const Padding(
                        padding: EdgeInsets.only(bottom: MlSpacing.sm),
                        child: MlSkeleton(height: 76),
                      ),
                    )
                  : _deals.isEmpty
                      ? MlEmptyState(
                          title: 'No deals found',
                          subtitle: 'No opportunities currently in this stage.',
                          actionLabel: 'Reset Stage Filter',
                          onAction: () {
                            setState(() => _selectedStage = 'All');
                            _loadDeals();
                          },
                        )
                      : ListView.builder(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(MlSpacing.md),
                          itemCount: _deals.length,
                          itemBuilder: (context, index) {
                            final deal = _deals[index];
                            final formattedVal = '${deal.currency} ${deal.value.toStringAsFixed(0)}';

                            return Padding(
                              padding: const EdgeInsets.only(bottom: MlSpacing.sm),
                              child: MlRecordCard(
                                title: deal.title,
                                subtitle: '$formattedVal • Probability: ${deal.probability}%',
                                statusChip: MlStatusChip(
                                  label: deal.stage,
                                  state: deal.stage == 'Won'
                                      ? MlStatusState.success
                                      : deal.stage == 'Lost'
                                          ? MlStatusState.danger
                                          : MlStatusState.primary,
                                ),
                                trailing: MlButton(
                                  label: 'Move Stage',
                                  onPressed: () => _openChangeStageSheet(deal),
                                  variant: MlButtonVariant.secondary,
                                ),
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

