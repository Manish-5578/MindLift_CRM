using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Rules;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Service for managing visual DAG workflows, traversing condition branches, and executing simulations.
/// </summary>
public sealed class VisualWorkflowService(
    AppDbContext db,
    ITenantContext tenant,
    SettingsVersion version) : IVisualWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IReadOnlyList<VisualWorkflowDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default)
    {
        var query = db.VisualWorkflows.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var norm = entityType.Trim().ToLowerInvariant();
            query = query.Where(x => x.EntityType == norm);
        }

        var rows = await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return rows.Select(MapWorkflow).ToList();
    }

    public async Task<VisualWorkflowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.VisualWorkflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Visual workflow", id);
        return MapWorkflow(row);
    }

    public async Task<VisualWorkflowDto> CreateAsync(SaveVisualWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var nodes = request.Nodes ?? [];
        var edges = request.Edges ?? [];

        ValidateGraph(nodes, edges);

        var row = new VisualWorkflow
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            EntityType = request.EntityType.Trim().ToLowerInvariant(),
            TriggerType = request.TriggerType.Trim().ToLowerInvariant(),
            TriggerConfigJson = request.TriggerConfigJson,
            IsActive = request.IsActive,
            NodesJson = JsonSerializer.Serialize(nodes, JsonOptions),
            EdgesJson = JsonSerializer.Serialize(edges, JsonOptions)
        };

        db.VisualWorkflows.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return MapWorkflow(row);
    }

    public async Task<VisualWorkflowDto> UpdateAsync(Guid id, SaveVisualWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var row = await db.VisualWorkflows.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Visual workflow", id);

        var nodes = request.Nodes ?? [];
        var edges = request.Edges ?? [];

        ValidateGraph(nodes, edges);

        row.Name = request.Name.Trim();
        row.Description = request.Description?.Trim();
        row.EntityType = request.EntityType.Trim().ToLowerInvariant();
        row.TriggerType = request.TriggerType.Trim().ToLowerInvariant();
        row.TriggerConfigJson = request.TriggerConfigJson;
        row.IsActive = request.IsActive;
        row.NodesJson = JsonSerializer.Serialize(nodes, JsonOptions);
        row.EdgesJson = JsonSerializer.Serialize(edges, JsonOptions);

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return MapWorkflow(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.VisualWorkflows.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Visual workflow", id);

        db.VisualWorkflows.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<WorkflowSimulationResult> SimulateAsync(Guid id, SimulateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var row = await db.VisualWorkflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Visual workflow", id);

        var nodes = ParseNodes(row.NodesJson);
        var edges = ParseEdges(row.EdgesJson);

        var currentFacts = request.Facts != null
            ? new Dictionary<string, object?>(request.Facts, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var executedNodeIds = new List<string>();
        var steps = new List<WorkflowStepExecutionDto>();

        if (nodes.Count == 0)
        {
            sw.Stop();
            return new WorkflowSimulationResult(true, "Completed", steps, executedNodeIds, currentFacts);
        }

        // Find root node: type "trigger", or node with no incoming edges, or first node
        var targetNodeIds = new HashSet<string>(edges.Select(e => e.TargetNodeId), StringComparer.OrdinalIgnoreCase);
        var startNode = nodes.FirstOrDefault(n => string.Equals(n.Type, "trigger", StringComparison.OrdinalIgnoreCase))
                        ?? nodes.FirstOrDefault(n => !targetNodeIds.Contains(n.Id))
                        ?? nodes[0];

        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var edgeList = edges.ToList();

        var currentNode = startNode;
        var visitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool hasError = false;
        string? errorMessage = null;

        while (currentNode != null)
        {
            var nodeId = currentNode.Id;
            visitCounts.TryGetValue(nodeId, out var count);
            if (count >= 10 || steps.Count >= 100)
            {
                hasError = true;
                errorMessage = "Execution aborted: loop or step threshold exceeded.";
                steps.Add(new WorkflowStepExecutionDto(
                    nodeId,
                    currentNode.Type,
                    currentNode.Label,
                    "Failed",
                    null,
                    errorMessage,
                    DateTime.UtcNow
                ));
                break;
            }
            visitCounts[nodeId] = count + 1;
            executedNodeIds.Add(nodeId);

            string? stepOutput = null;
            string? stepError = null;
            string stepStatus = "Success";
            string? preferredPort = null;

            try
            {
                var normType = currentNode.Type.Trim().ToLowerInvariant();
                switch (normType)
                {
                    case "trigger":
                        stepOutput = $"Workflow triggered on {currentNode.Label}.";
                        break;

                    case "condition":
                        bool isMatch;
                        if (currentNode.Condition != null)
                        {
                            isMatch = RuleEvaluator.Evaluate(currentNode.Condition, currentFacts);
                        }
                        else
                        {
                            isMatch = true;
                        }

                        preferredPort = isMatch ? "true" : "false";
                        stepOutput = $"Condition evaluated to: {isMatch} (selected port: {preferredPort}).";
                        break;

                    case "action_update_field":
                        if (currentNode.Config != null && currentNode.Config.TryGetValue("field", out var fld) && fld != null)
                        {
                            var fieldKey = fld.ToString()!;
                            currentNode.Config.TryGetValue("value", out var val);
                            currentFacts[fieldKey] = val;
                            stepOutput = $"Updated field '{fieldKey}' to '{val}'.";
                        }
                        else
                        {
                            stepOutput = "Field update skipped: configuration missing field key.";
                        }
                        break;

                    case "action_task":
                        var taskTitle = currentNode.Config?.GetValueOrDefault("title")?.ToString() ?? "Follow up task";
                        var dueDays = currentNode.Config?.GetValueOrDefault("dueDays")?.ToString() ?? "3";
                        stepOutput = $"Created task '{taskTitle}' (due in {dueDays} days).";
                        break;

                    case "action_email":
                        var to = currentNode.Config?.GetValueOrDefault("to")?.ToString() ?? "customer@example.com";
                        var subject = currentNode.Config?.GetValueOrDefault("subject")?.ToString() ?? "Update";
                        stepOutput = $"Queued email to '{to}' with subject '{subject}'.";
                        break;

                    case "action_notification":
                        var message = currentNode.Config?.GetValueOrDefault("message")?.ToString() ?? "Record updated";
                        stepOutput = $"Sent in-app notification: '{message}'.";
                        break;

                    case "action_webhook":
                        var url = currentNode.Config?.GetValueOrDefault("url")?.ToString() ?? "https://example.com/webhook";
                        var method = currentNode.Config?.GetValueOrDefault("method")?.ToString() ?? "POST";
                        stepOutput = $"Dispatched webhook: {method} {url}.";
                        break;

                    case "delay":
                        var duration = currentNode.Config?.GetValueOrDefault("duration")?.ToString() ?? "1";
                        var unit = currentNode.Config?.GetValueOrDefault("unit")?.ToString() ?? "days";
                        stepOutput = $"Execution delayed for {duration} {unit}.";
                        break;

                    default:
                        stepOutput = $"Executed step '{currentNode.Label}'.";
                        break;
                }
            }
            catch (Exception ex)
            {
                hasError = true;
                stepStatus = "Failed";
                stepError = ex.Message;
                errorMessage = ex.Message;
            }

            steps.Add(new WorkflowStepExecutionDto(
                nodeId,
                currentNode.Type,
                currentNode.Label,
                stepStatus,
                stepOutput,
                stepError,
                DateTime.UtcNow
            ));

            if (hasError)
            {
                break;
            }

            // Find next node via outgoing edge
            var outgoingEdges = edgeList.Where(e => string.Equals(e.SourceNodeId, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (outgoingEdges.Count == 0)
            {
                currentNode = null;
                break;
            }

            WorkflowEdgeDto? nextEdge = null;
            if (!string.IsNullOrWhiteSpace(preferredPort))
            {
                nextEdge = outgoingEdges.FirstOrDefault(e =>
                    string.Equals(e.SourcePort, preferredPort, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Label, preferredPort, StringComparison.OrdinalIgnoreCase));
            }

            nextEdge ??= outgoingEdges[0];

            if (nodeMap.TryGetValue(nextEdge.TargetNodeId, out var targetNode))
            {
                currentNode = targetNode;
            }
            else
            {
                currentNode = null;
            }
        }

        sw.Stop();

        if (request.RecordExecution)
        {
            var log = new VisualWorkflowExecution
            {
                WorkflowId = row.Id,
                WorkflowName = row.Name,
                EntityType = row.EntityType,
                EntityId = request.EntityId,
                Status = hasError ? "Failed" : "Success",
                ExecutedAt = DateTime.UtcNow,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = errorMessage,
                ExecutionLogJson = JsonSerializer.Serialize(steps, JsonOptions)
            };

            db.VisualWorkflowExecutions.Add(log);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new WorkflowSimulationResult(
            !hasError,
            hasError ? "Failed" : "Success",
            steps,
            executedNodeIds,
            currentFacts,
            errorMessage
        );
    }

    public async Task<IReadOnlyList<VisualWorkflowExecutionDto>> ListExecutionsAsync(Guid workflowId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var max = limit is < 1 or > 200 ? 50 : limit;
        var rows = await db.VisualWorkflowExecutions.AsNoTracking()
            .Where(x => x.WorkflowId == workflowId)
            .OrderByDescending(x => x.ExecutedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            List<WorkflowStepExecutionDto> steps;
            try
            {
                steps = JsonSerializer.Deserialize<List<WorkflowStepExecutionDto>>(r.ExecutionLogJson, JsonOptions) ?? [];
            }
            catch
            {
                steps = [];
            }

            return new VisualWorkflowExecutionDto(
                r.Id,
                r.WorkflowId,
                r.WorkflowName,
                r.EntityType,
                r.EntityId,
                r.Status,
                r.ExecutedAt,
                r.DurationMs,
                r.ErrorMessage,
                steps
            );
        }).ToList();
    }

    private static void ValidateRequest(SaveVisualWorkflowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.EntityType))
            throw new ValidationException("EntityType is required.");
        if (string.IsNullOrWhiteSpace(request.TriggerType))
            throw new ValidationException("TriggerType is required.");
    }

    private static void ValidateGraph(IReadOnlyList<WorkflowNodeDto> nodes, IReadOnlyList<WorkflowEdgeDto> edges)
    {
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id))
                throw new ValidationException("Node Id cannot be empty.");
            if (!nodeIds.Add(n.Id))
                throw new ValidationException($"Duplicate node Id '{n.Id}'.");
            if (string.IsNullOrWhiteSpace(n.Type))
                throw new ValidationException($"Node Type cannot be empty for node '{n.Id}'.");
        }

        foreach (var e in edges)
        {
            if (!nodeIds.Contains(e.SourceNodeId))
                throw new ValidationException($"Edge SourceNodeId '{e.SourceNodeId}' references a non-existent node.");
            if (!nodeIds.Contains(e.TargetNodeId))
                throw new ValidationException($"Edge TargetNodeId '{e.TargetNodeId}' references a non-existent node.");
        }
    }

    private static IReadOnlyList<WorkflowNodeDto> ParseNodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<WorkflowNodeDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<WorkflowEdgeDto> ParseEdges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<WorkflowEdgeDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static VisualWorkflowDto MapWorkflow(VisualWorkflow w)
    {
        return new VisualWorkflowDto(
            w.Id,
            w.Name,
            w.Description,
            w.EntityType,
            w.TriggerType,
            w.TriggerConfigJson,
            w.IsActive,
            ParseNodes(w.NodesJson),
            ParseEdges(w.EdgesJson),
            w.CreatedAt,
            w.UpdatedAt
        );
    }
}
