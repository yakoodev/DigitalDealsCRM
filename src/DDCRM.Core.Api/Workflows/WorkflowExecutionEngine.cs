using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;

namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowExecutionEngine(
    WorkflowNodeExecutorRegistry executors,
    ILogger<WorkflowExecutionEngine> logger)
{
    public async Task<Dictionary<string, object?>> ExecuteAsync(
        CoreDbContext dbContext,
        WorkflowExecutionEntity execution,
        WorkflowDefinitionEntity definition,
        WorkflowTriggerEventEntity triggerEvent,
        CancellationToken cancellationToken)
    {
        var published = definition.PublishedJson ?? definition.DraftJson;
        var workflow = JsonSerializer.Deserialize<WorkflowDraftModel>(published, JsonOptions())
            ?? throw new InvalidOperationException("Не удалось десериализовать workflow graph.");
        WorkflowGraphValidator.ValidateOrThrow(workflow);

        var nodeMap = workflow.Nodes.ToDictionary(node => node.Id.Trim(), StringComparer.Ordinal);
        var outgoingEdges = workflow.Edges
            .GroupBy(edge => edge.Source.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var startNode = WorkflowGraphValidator.ResolveStartNode(workflow, triggerEvent.Source);
        var context = CreateRuntimeContext(execution, triggerEvent);
        var startedAt = DateTimeOffset.UtcNow;
        var currentNodeId = startNode.Id.Trim();
        var stepIndex = 0;

        while (!string.IsNullOrWhiteSpace(currentNodeId))
        {
            if (stepIndex >= workflow.MaxSteps)
            {
                throw new InvalidOperationException($"Workflow guard exceeded: maxSteps={workflow.MaxSteps}.");
            }

            if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(workflow.MaxDurationSeconds))
            {
                throw new InvalidOperationException($"Workflow guard exceeded: maxDurationSeconds={workflow.MaxDurationSeconds}.");
            }

            if (!nodeMap.TryGetValue(currentNodeId, out var node))
            {
                throw new InvalidOperationException($"Workflow node `{currentNodeId}` не найден.");
            }

            var step = new WorkflowExecutionStepEntity
            {
                Id = Guid.NewGuid(),
                ExecutionId = execution.Id,
                NodeId = node.Id.Trim(),
                NodeType = node.Type.Trim(),
                StepIndex = stepIndex,
                Status = "running",
                StartedAtUtc = DateTimeOffset.UtcNow,
                InputJson = JsonSerializer.Serialize(context.Variables),
            };
            dbContext.WorkflowExecutionSteps.Add(step);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                var executor = executors.Resolve(node.Type.Trim());
                var result = await executor.ExecuteAsync(
                    new WorkflowNodeExecutionRequest(node, context, dbContext),
                    cancellationToken);

                if (result.Variables is not null)
                {
                    foreach (var pair in result.Variables)
                    {
                        context.Variables[pair.Key] = pair.Value;
                    }
                }

                step.Status = "completed";
                step.FinishedAtUtc = DateTimeOffset.UtcNow;
                step.OutputJson = JsonSerializer.Serialize(result.Variables ?? new Dictionary<string, object?>());

                await dbContext.SaveChangesAsync(cancellationToken);

                if (result.Stop)
                {
                    break;
                }

                currentNodeId = ResolveNextNodeId(
                    node,
                    outgoingEdges,
                    context.Variables);
                stepIndex += 1;
            }
            catch (Exception exception)
            {
                step.Status = "failed";
                step.Error = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
                step.FinishedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogWarning(
                    exception,
                    "Workflow execution failed at node {NodeId} for execution {ExecutionId}",
                    node.Id,
                    execution.Id);
                throw;
            }
        }

        return context.Variables;
    }

    private static WorkflowExecutionRuntimeContext CreateRuntimeContext(
        WorkflowExecutionEntity execution,
        WorkflowTriggerEventEntity triggerEvent)
    {
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = execution.ProjectId,
            OfferId = execution.OfferId,
            TriggerEventId = execution.TriggerEventId,
            SourceOrderId = execution.SourceOrderId,
        };

        context.Variables["projectId"] = execution.ProjectId.ToString();
        context.Variables["offerId"] = execution.OfferId.ToString();
        context.Variables["sourceOrderId"] = execution.SourceOrderId;
        context.Variables["triggerSource"] = triggerEvent.Source;
        context.Variables["buyerId"] = triggerEvent.BuyerId;
        context.Variables["event.source"] = triggerEvent.Source;
        context.Variables["event.type"] = ResolveEventType(triggerEvent.Source);

        Dictionary<string, object?>? parsedPayload = null;
        try
        {
            parsedPayload = JsonSerializer.Deserialize<Dictionary<string, object?>>(triggerEvent.PayloadJson, JsonOptions());
            context.Variables["payload"] = parsedPayload ?? new Dictionary<string, object?>();
        }
        catch
        {
            context.Variables["payload"] = triggerEvent.PayloadJson;
        }

        context.Variables["event.platform"] = ReadPayloadString(parsedPayload, "platform");
        context.Variables["event.quantity"] = ReadPayloadDecimal(parsedPayload, "quantity");
        context.Variables["event.amount"] = ReadPayloadDecimal(parsedPayload, "amount");
        context.Variables["event.currency"] = ReadPayloadString(parsedPayload, "currency");
        context.Variables["event.messageText"] = ReadPayloadString(parsedPayload, "messageText");
        context.Variables["event.reviewRating"] = ReadPayloadInt(parsedPayload, "reviewRating");
        context.Variables["event.reviewText"] = ReadPayloadString(parsedPayload, "reviewText");

        return context;
    }

    private static string? ResolveNextNodeId(
        WorkflowNodeModel sourceNode,
        IReadOnlyDictionary<string, List<WorkflowEdgeModel>> outgoingEdges,
        Dictionary<string, object?> variables)
    {
        var sourceNodeId = sourceNode.Id.Trim();
        if (!outgoingEdges.TryGetValue(sourceNodeId, out var edges) || edges.Count == 0)
        {
            return null;
        }

        var flowEdges = edges
            .Where(IsFlowEdge)
            .ToList();
        var routeCandidates = flowEdges.Count > 0 ? flowEdges : edges;

        if (string.Equals(sourceNode.Type.Trim(), WorkflowNodeTypes.Condition, StringComparison.Ordinal))
        {
            var conditionResult = TryResolveConditionResult(sourceNode, variables);
            if (conditionResult.HasValue)
            {
                var preferredHandle = conditionResult.Value ? "out-true" : "out-false";
                var preferredEdge = routeCandidates.FirstOrDefault(edge => HandleEquals(edge.SourceHandle, preferredHandle));
                if (preferredEdge is not null)
                {
                    return preferredEdge.Target.Trim();
                }

                var nextEdge = routeCandidates.FirstOrDefault(edge => HandleEquals(edge.SourceHandle, "out-next"));
                if (nextEdge is not null)
                {
                    return nextEdge.Target.Trim();
                }
            }
        }

        if (routeCandidates.Count == 1)
        {
            return routeCandidates[0].Target.Trim();
        }

        WorkflowEdgeModel? fallback = null;
        foreach (var edge in routeCandidates)
        {
            if (string.IsNullOrWhiteSpace(edge.Condition))
            {
                fallback ??= edge;
                continue;
            }

            if (EvaluateCondition(edge.Condition, variables))
            {
                return edge.Target.Trim();
            }
        }

        return fallback?.Target.Trim();
    }

    private static bool IsFlowEdge(WorkflowEdgeModel edge)
    {
        var sourceHandle = edge.SourceHandle?.Trim();
        if (string.IsNullOrWhiteSpace(sourceHandle))
        {
            // Backward compatibility: legacy drafts without handles are treated as flow edges.
            return true;
        }

        return sourceHandle.Equals("out-flow", StringComparison.OrdinalIgnoreCase)
               || sourceHandle.Equals("out-next", StringComparison.OrdinalIgnoreCase)
               || sourceHandle.Equals("out-true", StringComparison.OrdinalIgnoreCase)
               || sourceHandle.Equals("out-false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HandleEquals(string? sourceHandle, string expected)
    {
        return string.Equals(sourceHandle?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? TryResolveConditionResult(WorkflowNodeModel sourceNode, Dictionary<string, object?> variables)
    {
        var keyCandidates = new[]
        {
            $"condition:{sourceNode.Id}",
            $"condition:{sourceNode.Id.Trim()}",
        };

        foreach (var key in keyCandidates)
        {
            if (!variables.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            if (TryReadBool(value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryReadBool(object value, out bool result)
    {
        switch (value)
        {
            case bool boolValue:
                result = boolValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                result = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                result = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
            {
                if (bool.TryParse(element.GetString(), out var parsed))
                {
                    result = parsed;
                    return true;
                }

                break;
            }
            default:
            {
                if (bool.TryParse(value.ToString(), out var parsed))
                {
                    result = parsed;
                    return true;
                }

                break;
            }
        }

        result = false;
        return false;
    }

    private static bool EvaluateCondition(string condition, Dictionary<string, object?> variables)
    {
        var normalized = condition.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var separatorIndex = normalized.IndexOf("==", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var key = normalized[..separatorIndex].Trim();
            var expected = normalized[(separatorIndex + 2)..].Trim().Trim('"', '\'');
            if (!variables.TryGetValue(key, out var actual) || actual is null)
            {
                return false;
            }

            return string.Equals(actual.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        if (!variables.TryGetValue(normalized, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool boolValue => boolValue,
            JsonElement element when element.ValueKind == JsonValueKind.True => true,
            JsonElement element when element.ValueKind == JsonValueKind.String
                => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string ResolveEventType(string triggerSource)
    {
        if (triggerSource.Contains("message", StringComparison.OrdinalIgnoreCase))
        {
            return "message";
        }

        if (triggerSource.Contains("review", StringComparison.OrdinalIgnoreCase))
        {
            return "review";
        }

        return "purchase";
    }

    private static string? ReadPayloadString(Dictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string asString => asString,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString(),
        };
    }

    private static decimal? ReadPayloadDecimal(Dictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => Convert.ToDecimal(floatValue),
            double doubleValue => Convert.ToDecimal(doubleValue),
            decimal decimalValue => decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsed) => parsed,
            string asString when decimal.TryParse(asString, out var parsed) => parsed,
            _ => null,
        };
    }

    private static int? ReadPayloadInt(Dictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            decimal decimalValue => (int)decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed) => parsed,
            string asString when int.TryParse(asString, out var parsed) => parsed,
            _ => null,
        };
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
