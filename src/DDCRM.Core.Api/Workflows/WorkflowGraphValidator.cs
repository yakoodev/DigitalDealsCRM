using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;

namespace DDCRM.Core.Api.Workflows;

public static class WorkflowGraphValidator
{
    private const double MaxAbsNodeCoordinate = 100_000d;
    private const double MaxAbsViewportCoordinate = 1_000_000d;
    private const double MinViewportZoom = 0.1d;
    private const double MaxViewportZoom = 4d;
    private static readonly HashSet<string> StartNodeTypes = new(StringComparer.Ordinal)
    {
        WorkflowNodeTypes.PurchaseStart,
        WorkflowNodeTypes.MessageStart,
        WorkflowNodeTypes.ReviewStart,
    };

    public static void ValidateOrThrow(WorkflowDraftModel draft)
    {
        if (draft is null)
        {
            ThrowValidation("workflow draft обязателен.");
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.Version))
        {
            ThrowValidation("workflow.version обязателен.");
        }

        if (draft.MaxSteps is < 1 or > 5000)
        {
            ThrowValidation("workflow.maxSteps должен быть в диапазоне 1..5000.");
        }

        if (draft.MaxDurationSeconds is < 1 or > 3600)
        {
            ThrowValidation("workflow.maxDurationSeconds должен быть в диапазоне 1..3600.");
        }

        if (draft.MaxRetries is < 0 or > 20)
        {
            ThrowValidation("workflow.maxRetries должен быть в диапазоне 0..20.");
        }

        if (draft.Nodes.Count == 0)
        {
            ThrowValidation("workflow.nodes должен содержать минимум один узел.");
        }

        var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeTypeById = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasEndNode = false;
        foreach (var node in draft.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                ThrowValidation("workflow.nodes[].id обязателен.");
            }

            if (!seenNodeIds.Add(node.Id.Trim()))
            {
                ThrowValidation($"workflow.nodes[].id `{node.Id}` повторяется.");
            }

            var normalizedNodeType = node.Type?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedNodeType) || !WorkflowNodeTypes.All.Contains(normalizedNodeType))
            {
                ThrowValidation($"workflow.nodes[].type `{node.Type}` не поддерживается.");
            }

            nodeTypeById[node.Id.Trim()] = normalizedNodeType;

            if (string.Equals(normalizedNodeType, WorkflowNodeTypes.End, StringComparison.Ordinal))
            {
                hasEndNode = true;
            }

            ValidateNodeUiOrThrow(node);
        }

        if (!hasEndNode)
        {
            ThrowValidation("workflow должен содержать хотя бы один узел типа End.");
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in draft.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                ThrowValidation("workflow.edges[].id обязателен.");
            }

            if (!edgeIds.Add(edge.Id.Trim()))
            {
                ThrowValidation($"workflow.edges[].id `{edge.Id}` повторяется.");
            }

            if (string.IsNullOrWhiteSpace(edge.Source) || !seenNodeIds.Contains(edge.Source.Trim()))
            {
                ThrowValidation($"workflow.edges[].source `{edge.Source}` не найден среди nodes.");
            }

            if (string.IsNullOrWhiteSpace(edge.Target) || !seenNodeIds.Contains(edge.Target.Trim()))
            {
                ThrowValidation($"workflow.edges[].target `{edge.Target}` не найден среди nodes.");
            }

            if (!string.IsNullOrWhiteSpace(edge.SourceHandle) && edge.SourceHandle.Trim().Length > 120)
            {
                ThrowValidation("workflow.edges[].sourceHandle слишком длинный (максимум 120 символов).");
            }

            if (!string.IsNullOrWhiteSpace(edge.TargetHandle) && edge.TargetHandle.Trim().Length > 120)
            {
                ThrowValidation("workflow.edges[].targetHandle слишком длинный (максимум 120 символов).");
            }
        }

        var incoming = new HashSet<string>(
            draft.Edges
                .Select(edge => edge.Target.Trim()),
            StringComparer.Ordinal);
        var roots = draft.Nodes
            .Where(node => !incoming.Contains(node.Id.Trim()))
            .ToArray();
        if (roots.Length == 0)
        {
            ThrowValidation("workflow должен содержать минимум один корневой узел без входящих ребер.");
        }

        var nonRootStartNode = draft.Nodes
            .FirstOrDefault(node =>
                IsStartNodeType(node.Type.Trim())
                && incoming.Contains(node.Id.Trim()));
        if (nonRootStartNode is not null)
        {
            ThrowValidation($"Стартовый node `{nonRootStartNode.Id}` должен быть корневым (без входящих ребер).");
        }

        var startRoots = roots
            .Where(node => IsStartNodeType(node.Type.Trim()))
            .ToArray();
        if (startRoots.Length == 0)
        {
            ThrowValidation("workflow должен содержать хотя бы один стартовый node (PurchaseStart/MessageStart/ReviewStart).");
        }

        var duplicatedStartType = startRoots
            .GroupBy(node => node.Type.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedStartType is not null)
        {
            ThrowValidation($"workflow содержит несколько стартовых nodes типа `{duplicatedStartType.Key}`. Допускается не более одного.");
        }

        ValidateDraftUiOrThrow(draft.Ui, seenNodeIds, nodeTypeById, incoming);
    }

    public static WorkflowNodeModel ResolveStartNode(WorkflowDraftModel draft, string? triggerSource = null)
    {
        var incoming = new HashSet<string>(
            draft.Edges.Select(edge => edge.Target.Trim()),
            StringComparer.Ordinal);
        var rootNodes = draft.Nodes
            .Where(node => !incoming.Contains(node.Id.Trim()))
            .ToArray();
        var rootStartNodes = rootNodes
            .Where(node => IsStartNodeType(node.Type.Trim()))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var configuredEntryNodeId = draft.Ui?.EntryNodeId?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredEntryNodeId))
        {
            var configuredRoot = rootStartNodes
                .SingleOrDefault(node => string.Equals(node.Id.Trim(), configuredEntryNodeId, StringComparison.Ordinal));

            if (configuredRoot is null)
            {
                ThrowValidation("workflow.ui.entryNodeId должен ссылаться на стартовый корневой node.");
            }

            return configuredRoot!;
        }

        var requestedStartNodeType = ResolvePreferredStartNodeType(triggerSource);
        if (!string.IsNullOrWhiteSpace(requestedStartNodeType))
        {
            var matched = rootStartNodes.FirstOrDefault(node =>
                string.Equals(node.Type.Trim(), requestedStartNodeType, StringComparison.Ordinal));
            if (matched is not null)
            {
                return matched;
            }
        }

        var purchaseStart = rootStartNodes
            .FirstOrDefault(node => string.Equals(node.Type.Trim(), WorkflowNodeTypes.PurchaseStart, StringComparison.Ordinal));
        if (purchaseStart is not null)
        {
            return purchaseStart;
        }

        if (rootStartNodes.Length > 0)
        {
            return rootStartNodes[0];
        }

        return rootNodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .First();
    }

    private static void ThrowValidation(string message)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);
    }

    public static void ValidateUiOrThrow(WorkflowDraftModel draft)
    {
        if (draft is null)
        {
            return;
        }

        var seenNodeIds = new HashSet<string>(
            draft.Nodes.Select(node => node.Id.Trim()),
            StringComparer.Ordinal);
        var nodeTypeById = draft.Nodes
            .ToDictionary(node => node.Id.Trim(), node => node.Type.Trim(), StringComparer.Ordinal);
        var incoming = new HashSet<string>(
            draft.Edges.Select(edge => edge.Target.Trim()),
            StringComparer.Ordinal);

        foreach (var node in draft.Nodes)
        {
            ValidateNodeUiOrThrow(node);
        }

        ValidateDraftUiOrThrow(draft.Ui, seenNodeIds, nodeTypeById, incoming);
    }

    private static void ValidateNodeUiOrThrow(WorkflowNodeModel node)
    {
        var position = node.Ui?.Position;
        if (position is null)
        {
            return;
        }

        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            ThrowValidation($"workflow.nodes[].ui.position для node `{node.Id}` должен содержать finite-координаты.");
        }

        if (Math.Abs(position.X) > MaxAbsNodeCoordinate || Math.Abs(position.Y) > MaxAbsNodeCoordinate)
        {
            ThrowValidation($"workflow.nodes[].ui.position для node `{node.Id}` выходит за допустимые пределы.");
        }
    }

    private static void ValidateDraftUiOrThrow(
        WorkflowDraftUiModel? ui,
        IReadOnlySet<string> nodeIds,
        IReadOnlyDictionary<string, string> nodeTypesById,
        IReadOnlySet<string> incomingNodeIds)
    {
        if (!string.IsNullOrWhiteSpace(ui?.EntryNodeId))
        {
            var entryNodeId = ui.EntryNodeId.Trim();
            if (!nodeIds.Contains(entryNodeId))
            {
                ThrowValidation("workflow.ui.entryNodeId должен ссылаться на существующий node.");
            }

            if (incomingNodeIds.Contains(entryNodeId))
            {
                ThrowValidation("workflow.ui.entryNodeId должен ссылаться на node без входящих ребер.");
            }

            if (!nodeTypesById.TryGetValue(entryNodeId, out var entryNodeType)
                || !IsStartNodeType(entryNodeType))
            {
                ThrowValidation("workflow.ui.entryNodeId должен ссылаться на стартовый node (PurchaseStart/MessageStart/ReviewStart).");
            }
        }

        var viewport = ui?.Viewport;
        if (viewport is null)
        {
            return;
        }

        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Zoom))
        {
            ThrowValidation("workflow.ui.viewport должен содержать finite-значения.");
        }

        if (Math.Abs(viewport.X) > MaxAbsViewportCoordinate || Math.Abs(viewport.Y) > MaxAbsViewportCoordinate)
        {
            ThrowValidation("workflow.ui.viewport.x/y выходят за допустимые пределы.");
        }

        if (viewport.Zoom < MinViewportZoom || viewport.Zoom > MaxViewportZoom)
        {
            ThrowValidation($"workflow.ui.viewport.zoom должен быть в диапазоне {MinViewportZoom:0.0}..{MaxViewportZoom:0.0}.");
        }
    }

    private static bool IsStartNodeType(string nodeType)
    {
        return StartNodeTypes.Contains(nodeType);
    }

    private static string? ResolvePreferredStartNodeType(string? triggerSource)
    {
        if (string.IsNullOrWhiteSpace(triggerSource))
        {
            return null;
        }

        var normalized = triggerSource.Trim().ToLowerInvariant();
        if (normalized.Contains("message", StringComparison.Ordinal))
        {
            return WorkflowNodeTypes.MessageStart;
        }

        if (normalized.Contains("review", StringComparison.Ordinal))
        {
            return WorkflowNodeTypes.ReviewStart;
        }

        if (normalized.Contains("purchase", StringComparison.Ordinal))
        {
            return WorkflowNodeTypes.PurchaseStart;
        }

        return null;
    }
}
