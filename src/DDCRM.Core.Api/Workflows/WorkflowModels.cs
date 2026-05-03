using System.Text.Json;

namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowDraftModel
{
    public string Version { get; set; } = "v1";

    public List<WorkflowNodeModel> Nodes { get; set; } = [];

    public List<WorkflowEdgeModel> Edges { get; set; } = [];

    public int MaxSteps { get; set; } = 128;

    public int MaxDurationSeconds { get; set; } = 120;

    public int MaxRetries { get; set; } = 3;

    public WorkflowDraftUiModel? Ui { get; set; }
}

public sealed class WorkflowNodeModel
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Name { get; set; }

    public Dictionary<string, JsonElement>? Config { get; set; }

    public WorkflowNodeUiModel? Ui { get; set; }
}

public sealed class WorkflowEdgeModel
{
    public string Id { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? SourceHandle { get; set; }

    public string Target { get; set; } = string.Empty;

    public string? TargetHandle { get; set; }

    public string? Condition { get; set; }
}

public sealed class WorkflowNodeUiModel
{
    public WorkflowNodePositionModel? Position { get; set; }
}

public sealed class WorkflowNodePositionModel
{
    public double X { get; set; }

    public double Y { get; set; }
}

public sealed class WorkflowDraftUiModel
{
    public WorkflowViewportModel? Viewport { get; set; }

    public string? EntryNodeId { get; set; }
}

public sealed class WorkflowViewportModel
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Zoom { get; set; }
}

public sealed class WorkflowExecutionRuntimeContext
{
    public Guid ProjectId { get; init; }

    public Guid OfferId { get; init; }

    public Guid TriggerEventId { get; init; }

    public string SourceOrderId { get; init; } = string.Empty;

    public Dictionary<string, object?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record WorkflowNodeExecutionResult(
    IDictionary<string, object?>? Variables = null,
    bool Stop = false);

public static class WorkflowNodeTypes
{
    public const string PurchaseStart = "PurchaseStart";
    public const string MessageStart = "MessageStart";
    public const string ReviewStart = "ReviewStart";
    public const string Condition = "Condition";
    public const string SetVariables = "SetVariables";
    public const string LoadOffer = "LoadOffer";
    public const string SelectAccountPriorityFallback = "SelectAccountPriorityFallback";
    public const string InvokeWorkerAction = "InvokeWorkerAction";
    public const string InvokeCustomHttp = "InvokeCustomHttp";
    public const string SteamAction = "SteamAction";
    public const string Task = "Task";
    public const string SendBuyerResponse = "SendBuyerResponse";
    public const string Notify = "Notify";
    public const string End = "End";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        PurchaseStart,
        MessageStart,
        ReviewStart,
        Condition,
        SetVariables,
        LoadOffer,
        SelectAccountPriorityFallback,
        InvokeWorkerAction,
        InvokeCustomHttp,
        SteamAction,
        Task,
        SendBuyerResponse,
        Notify,
        End,
    };
}
