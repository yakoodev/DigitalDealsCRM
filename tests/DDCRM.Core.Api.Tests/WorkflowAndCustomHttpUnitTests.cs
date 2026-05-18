using System.Net;
using System.Text.Json;
using DDCRM.Core.Api.Integrations;
using DDCRM.Core.Api.Workflows;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Tests;

public sealed class WorkflowAndCustomHttpUnitTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowGraphValidator_WithoutEndNode_ThrowsValidationError()
    {
        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 100,
            MaxDurationSeconds = 120,
            MaxRetries = 3,
            Nodes =
            [
                new WorkflowNodeModel { Id = "start", Type = WorkflowNodeTypes.LoadOffer },
            ],
            Edges =
            [
            ],
        };

        var exception = Assert.Throws<DDCRM.Shared.Errors.ApiErrorException>(() => WorkflowGraphValidator.ValidateOrThrow(draft));
        Assert.Contains("узел типа End", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SelectAccountPriorityFallbackNodeExecutor_SelectsPreferredPlatformThenFallback()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-node-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);
        var executor = new SelectAccountPriorityFallbackNodeExecutor();
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-1",
        };

        context.Variables["offerVariants"] = new List<WorkflowOfferVariantValue>
        {
            new(
                OfferVariantId: Guid.Parse("00000000-0000-0000-0000-000000000011"),
                AccountId: Guid.Parse("00000000-0000-0000-0000-000000000101"),
                WorkerProductId: "funpay-prod",
                Platform: "funpay",
                ObservedPrice: 100m,
                ObservedCurrency: "RUB",
                Priority: 20,
                IsActive: true),
            new(
                OfferVariantId: Guid.Parse("00000000-0000-0000-0000-000000000022"),
                AccountId: Guid.Parse("00000000-0000-0000-0000-000000000202"),
                WorkerProductId: "steam-prod",
                Platform: "steam",
                ObservedPrice: 110m,
                ObservedCurrency: "RUB",
                Priority: 10,
                IsActive: true),
        };

        var preferredNode = new WorkflowNodeModel
        {
            Id = "select",
            Type = WorkflowNodeTypes.SelectAccountPriorityFallback,
            Config = new Dictionary<string, JsonElement>
            {
                ["platform"] = JsonSerializer.SerializeToElement("funpay"),
            },
        };

        var preferredResult = await executor.ExecuteAsync(
            new WorkflowNodeExecutionRequest(preferredNode, context, dbContext),
            CancellationToken.None);

        Assert.NotNull(preferredResult.Variables);
        Assert.Equal("funpay", preferredResult.Variables!["selectedVariant.platform"]?.ToString());

        var fallbackNode = new WorkflowNodeModel
        {
            Id = "select-fallback",
            Type = WorkflowNodeTypes.SelectAccountPriorityFallback,
        };

        var fallbackResult = await executor.ExecuteAsync(
            new WorkflowNodeExecutionRequest(fallbackNode, context, dbContext),
            CancellationToken.None);

        Assert.NotNull(fallbackResult.Variables);
        Assert.Equal("steam", fallbackResult.Variables!["selectedVariant.platform"]?.ToString());
        Assert.Equal("steam-prod", fallbackResult.Variables["selectedVariant.workerProductId"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PurchaseStartNodeExecutor_ExposesPurchaseContextVariables()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-purchase-start-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);
        var executor = new PurchaseStartNodeExecutor();
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000321"),
            OfferId = Guid.Parse("00000000-0000-0000-0000-000000000654"),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-77",
        };
        context.Variables["buyerId"] = "buyer-42";
        context.Variables["payload"] = new Dictionary<string, object?>
        {
            ["quantity"] = 1,
        };
        context.Variables["triggerSource"] = "purchase-webhook";

        var node = new WorkflowNodeModel
        {
            Id = "purchase-start",
            Type = WorkflowNodeTypes.PurchaseStart,
        };

        var result = await executor.ExecuteAsync(new WorkflowNodeExecutionRequest(node, context, dbContext), CancellationToken.None);
        Assert.NotNull(result.Variables);
        Assert.Equal("order-77", result.Variables!["purchase.sourceOrderId"]?.ToString());
        Assert.Equal("buyer-42", result.Variables["purchase.buyerId"]?.ToString());
        Assert.Equal("purchase-webhook", result.Variables["purchase.triggerSource"]?.ToString());
        Assert.Equal("00000000-0000-0000-0000-000000000321", result.Variables["purchase.projectId"]?.ToString());
        Assert.Equal("00000000-0000-0000-0000-000000000654", result.Variables["purchase.offerId"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStartNodeExecutor_ExposesMessageContextVariables()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-message-start-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);
        var executor = new MessageStartNodeExecutor();
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-msg-1",
        };
        context.Variables["event.platform"] = "steam";
        context.Variables["event.messageText"] = "Где мой заказ?";
        context.Variables["event.quantity"] = 2;
        context.Variables["event.amount"] = 499.9m;
        context.Variables["event.currency"] = "RUB";
        context.Variables["buyerId"] = "buyer-9000";
        context.Variables["payload"] = new Dictionary<string, object?>
        {
            ["channel"] = "chat",
            ["chatId"] = "tg-chat-1",
            ["conversationId"] = "conv-777",
            ["accountId"] = "acc-555",
        };

        var node = new WorkflowNodeModel
        {
            Id = "message-start",
            Type = WorkflowNodeTypes.MessageStart,
        };

        var result = await executor.ExecuteAsync(new WorkflowNodeExecutionRequest(node, context, dbContext), CancellationToken.None);
        Assert.NotNull(result.Variables);
        Assert.Equal("message", result.Variables!["event.type"]?.ToString());
        Assert.Equal("steam", result.Variables["event.platform"]?.ToString());
        Assert.Equal("2", result.Variables["event.quantity"]?.ToString());
        Assert.Equal(499.9m, Convert.ToDecimal(result.Variables["event.amount"]));
        Assert.Equal("RUB", result.Variables["event.currency"]?.ToString());
        Assert.Equal("Где мой заказ?", result.Variables["message.text"]?.ToString());
        Assert.Equal("buyer-9000", result.Variables["message.buyerId"]?.ToString());
        Assert.Equal("order-msg-1", result.Variables["message.sourceOrderId"]?.ToString());
        Assert.Equal("tg-chat-1", result.Variables["message.chatId"]?.ToString());
        Assert.Equal("conv-777", result.Variables["message.conversationId"]?.ToString());
        Assert.Equal("acc-555", result.Variables["message.accountId"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TaskNodeExecutor_SchedulesTaskWithDelay()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-task-node-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);
        var executor = new TaskNodeExecutor();
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-task-1",
        };

        var node = new WorkflowNodeModel
        {
            Id = "task-1",
            Type = WorkflowNodeTypes.Task,
            Config = new Dictionary<string, JsonElement>
            {
                ["taskType"] = JsonSerializer.SerializeToElement("steam.change-password"),
                ["delaySeconds"] = JsonSerializer.SerializeToElement(10800),
            },
        };

        var result = await executor.ExecuteAsync(new WorkflowNodeExecutionRequest(node, context, dbContext), CancellationToken.None);
        Assert.NotNull(result.Variables);
        Assert.Equal("scheduled", result.Variables!["task.status"]?.ToString());
        Assert.Equal("steam.change-password", result.Variables["task.type"]?.ToString());
        Assert.Equal("10800", result.Variables["task.delaySeconds"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SteamActionNodeExecutor_RequiresActiveSteamGrant()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-steam-node-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);
        var bridgeClient = new WorkflowWorkerBridgeClient(new HttpClient(), NullLogger<WorkflowWorkerBridgeClient>.Instance);
        var executor = new SteamActionNodeExecutor(
            bridgeClient,
            Options.Create(new WorkflowMessagePollingOptions()));
        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-steam-1",
        };

        var node = new WorkflowNodeModel
        {
            Id = "steam-action-1",
            Type = WorkflowNodeTypes.SteamAction,
            Config = new Dictionary<string, JsonElement>
            {
                ["action"] = JsonSerializer.SerializeToElement("change-password"),
            },
        };

        var exception = await Assert.ThrowsAsync<DDCRM.Shared.Errors.ApiErrorException>(() =>
            executor.ExecuteAsync(new WorkflowNodeExecutionRequest(node, context, dbContext), CancellationToken.None));
        Assert.Contains("steam-accounts-manager", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowRuntimeTemplateResolver_ResolvesNestedPayloadAndTypedValues()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["payload"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["leaseId"] = "lease-42",
                ["durationMinutes"] = 120,
            },
            ["message.conversationId"] = "conv-1",
        };

        var leaseIdValue = WorkflowRuntimeTemplateResolver.ResolveStringTemplateValue("{{payload.leaseId}}", variables);
        Assert.Equal("lease-42", leaseIdValue?.ToString());

        var durationValue = WorkflowRuntimeTemplateResolver.ResolveStringTemplateValue("{{payload.durationMinutes}}", variables);
        Assert.Equal("120", durationValue?.ToString());

        var rendered = WorkflowRuntimeTemplateResolver.RenderStringTemplate(
            "lease={{payload.leaseId}}, conv={{message.conversationId}}",
            variables);
        Assert.Equal("lease=lease-42, conv=conv-1", rendered);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowAuditRedactor_RedactsSensitiveFields()
    {
        var payload = new Dictionary<string, object?>
        {
            ["credentials"] = new Dictionary<string, object?>
            {
                ["login"] = "user1",
                ["password"] = "super-secret-password",
            },
            ["payload"] = new Dictionary<string, object?>
            {
                ["sharedSecret"] = "abc",
                ["accessToken"] = "token-value",
            },
        };

        var json = WorkflowAuditRedactor.SerializeRedacted(payload);
        Assert.DoesNotContain("super-secret-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", json, StringComparison.Ordinal);
        Assert.Contains("***redacted***", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendBuyerResponseNodeExecutor_RendersGenericRuntimePlaceholders()
    {
        var dbOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-send-buyer-template-tests-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new CoreDbContext(dbOptions);

        var cryptoConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CORE_SECRETS_ENCRYPTION_KEY"] = "unit-test-secret-key-1234567890",
            })
            .Build();

        var executor = new SendBuyerResponseNodeExecutor(
            new TelegramNotificationSender(
                Options.Create(new TelegramNotificationOptions
                {
                    Enabled = false,
                    BotToken = "dummy-token",
                }),
                NullLogger<TelegramNotificationSender>.Instance),
            new ProjectSecretCrypto(cryptoConfig),
            new WorkflowWorkerBridgeClient(new HttpClient(), NullLogger<WorkflowWorkerBridgeClient>.Instance),
            Options.Create(new WorkflowMessagePollingOptions
            {
                DispatchWorkerReplies = false,
            }),
            NullLogger<SendBuyerResponseNodeExecutor>.Instance);

        var context = new WorkflowExecutionRuntimeContext
        {
            ProjectId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            SourceOrderId = "order-send-1",
        };
        context.Variables["rental.login"] = "steam_login";
        context.Variables["rental.password"] = "steam_password";
        context.Variables["steam.action.listText"] = "1. account-a";

        var node = new WorkflowNodeModel
        {
            Id = "send-buyer-1",
            Type = WorkflowNodeTypes.SendBuyerResponse,
            Config = new Dictionary<string, JsonElement>
            {
                ["message"] = JsonSerializer.SerializeToElement(
                    "Логин: {{rental.login}}, пароль: {{rental.password}}, список: {{steam.action.listText}}"),
            },
        };

        var result = await executor.ExecuteAsync(
            new WorkflowNodeExecutionRequest(node, context, dbContext),
            CancellationToken.None);

        Assert.NotNull(result.Variables);
        Assert.Equal(
            "Логин: steam_login, пароль: steam_password, список: 1. account-a",
            result.Variables!["buyerResponse.message"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CustomHttpValidation_RejectsInsecureAndPrivateEndpoints()
    {
        var allowlist = new[] { "127.0.0.1", "*.example.com" };

        var insecureEndpoint = new Uri("http://api.example.com/orders");
        var insecureException = await Assert.ThrowsAsync<DDCRM.Shared.Errors.ApiErrorException>(() =>
            CustomHttpIntegrationInvoker.ValidateTargetUriAsync(insecureEndpoint, allowlist, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.BadRequest, insecureException.StatusCode);

        var privateEndpoint = new Uri("https://127.0.0.1/orders");
        var privateException = await Assert.ThrowsAsync<DDCRM.Shared.Errors.ApiErrorException>(() =>
            CustomHttpIntegrationInvoker.ValidateTargetUriAsync(privateEndpoint, allowlist, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.BadRequest, privateException.StatusCode);

        var outsideAllowlist = new Uri("https://api.not-allowed.tld/orders");
        var outsideException = await Assert.ThrowsAsync<DDCRM.Shared.Errors.ApiErrorException>(() =>
            CustomHttpIntegrationInvoker.ValidateTargetUriAsync(outsideAllowlist, allowlist, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, outsideException.StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowGraphValidator_InvalidUiViewport_ThrowsValidationError()
    {
        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 100,
            MaxDurationSeconds = 120,
            MaxRetries = 3,
            Nodes =
            [
                new WorkflowNodeModel
                {
                    Id = "purchase-start",
                    Type = WorkflowNodeTypes.PurchaseStart,
                    Ui = new WorkflowNodeUiModel
                    {
                        Position = new WorkflowNodePositionModel
                        {
                            X = 0,
                            Y = 0,
                        },
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "end",
                    Type = WorkflowNodeTypes.End,
                    Ui = new WorkflowNodeUiModel
                    {
                        Position = new WorkflowNodePositionModel
                        {
                            X = 20,
                            Y = 40,
                        },
                    },
                },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-1",
                    Source = "purchase-start",
                    Target = "end",
                },
            ],
            Ui = new WorkflowDraftUiModel
            {
                Viewport = new WorkflowViewportModel
                {
                    X = 0,
                    Y = 0,
                    Zoom = 8,
                },
            },
        };

        var exception = Assert.Throws<DDCRM.Shared.Errors.ApiErrorException>(() => WorkflowGraphValidator.ValidateOrThrow(draft));
        Assert.Contains("workflow.ui.viewport.zoom", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowGraphValidator_InvalidEntryNodeId_ThrowsValidationError()
    {
        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 100,
            MaxDurationSeconds = 120,
            MaxRetries = 3,
            Nodes =
            [
                new WorkflowNodeModel
                {
                    Id = "purchase-start",
                    Type = WorkflowNodeTypes.PurchaseStart,
                },
                new WorkflowNodeModel
                {
                    Id = "end",
                    Type = WorkflowNodeTypes.End,
                },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-1",
                    Source = "purchase-start",
                    Target = "end",
                },
            ],
            Ui = new WorkflowDraftUiModel
            {
                EntryNodeId = "end",
            },
        };

        var exception = Assert.Throws<DDCRM.Shared.Errors.ApiErrorException>(() => WorkflowGraphValidator.ValidateOrThrow(draft));
        Assert.Contains("entryNodeId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowGraphValidator_DuplicateStartType_ThrowsValidationError()
    {
        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 100,
            MaxDurationSeconds = 120,
            MaxRetries = 3,
            Nodes =
            [
                new WorkflowNodeModel { Id = "purchase-1", Type = WorkflowNodeTypes.PurchaseStart },
                new WorkflowNodeModel { Id = "purchase-2", Type = WorkflowNodeTypes.PurchaseStart },
                new WorkflowNodeModel { Id = "end", Type = WorkflowNodeTypes.End },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-1",
                    Source = "purchase-1",
                    Target = "end",
                },
            ],
        };

        var exception = Assert.Throws<DDCRM.Shared.Errors.ApiErrorException>(() => WorkflowGraphValidator.ValidateOrThrow(draft));
        Assert.Contains("несколько стартовых", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WorkflowDraft_SerializationRoundtrip_PreservesUiLayout()
    {
        var draft = new WorkflowDraftModel
        {
            Version = "v2",
            MaxSteps = 150,
            MaxDurationSeconds = 180,
            MaxRetries = 2,
            Nodes =
            [
                new WorkflowNodeModel
                {
                    Id = "cond-1",
                    Type = WorkflowNodeTypes.Condition,
                    Config = new Dictionary<string, JsonElement>
                    {
                        ["field"] = JsonSerializer.SerializeToElement("platform"),
                        ["equals"] = JsonSerializer.SerializeToElement("steam"),
                    },
                    Ui = new WorkflowNodeUiModel
                    {
                        Position = new WorkflowNodePositionModel
                        {
                            X = 120.5,
                            Y = -42.25,
                        },
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "end",
                    Type = WorkflowNodeTypes.End,
                    Ui = new WorkflowNodeUiModel
                    {
                        Position = new WorkflowNodePositionModel
                        {
                            X = 450,
                            Y = 120,
                        },
                    },
                },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-1",
                    Source = "cond-1",
                    SourceHandle = "out-true",
                    Target = "end",
                    TargetHandle = "in-flow",
                },
            ],
            Ui = new WorkflowDraftUiModel
            {
                EntryNodeId = "cond-1",
                Viewport = new WorkflowViewportModel
                {
                    X = -200,
                    Y = 50,
                    Zoom = 1.2,
                },
            },
        };

        var json = JsonSerializer.Serialize(draft, WorkflowExecutionEngine.JsonOptions());
        var restored = JsonSerializer.Deserialize<WorkflowDraftModel>(json, WorkflowExecutionEngine.JsonOptions());
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Nodes.Count);
        Assert.Equal(120.5, restored.Nodes[0].Ui?.Position?.X);
        Assert.Equal(-42.25, restored.Nodes[0].Ui?.Position?.Y);
        Assert.Equal(-200, restored.Ui?.Viewport?.X);
        Assert.Equal(50, restored.Ui?.Viewport?.Y);
        Assert.Equal(1.2, restored.Ui?.Viewport?.Zoom);
        Assert.Equal("cond-1", restored.Ui?.EntryNodeId);
        Assert.Equal("out-true", restored.Edges[0].SourceHandle);
        Assert.Equal("in-flow", restored.Edges[0].TargetHandle);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProjectSecretCrypto_EncryptDecryptRoundtrip_Works()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CORE_SECRETS_ENCRYPTION_KEY"] = "unit-test-secret-key-1234567890",
            })
            .Build();

        var crypto = new ProjectSecretCrypto(configuration);
        const string plaintext = "my-sensitive-token";

        var ciphertext = crypto.Encrypt(plaintext);
        Assert.NotEqual(plaintext, ciphertext);

        var decrypted = crypto.Decrypt(ciphertext);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WorkflowExecutionEngine_UsesFlowHandles_AndIgnoresDataEdges()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-engine-routing-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);

        var projectId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var triggerEvent = new WorkflowTriggerEventEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            Source = "message-webhook",
            SourceOrderId = "order-msg-routing",
            PayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["platform"] = "steam",
                ["messageText"] = "!help",
            }),
            Status = "accepted",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 16,
            MaxDurationSeconds = 60,
            MaxRetries = 1,
            Nodes =
            [
                new WorkflowNodeModel
                {
                    Id = "message-start",
                    Type = WorkflowNodeTypes.MessageStart,
                },
                new WorkflowNodeModel
                {
                    Id = "condition",
                    Type = WorkflowNodeTypes.Condition,
                    Config = new Dictionary<string, JsonElement>
                    {
                        ["field"] = JsonSerializer.SerializeToElement("message.text"),
                        ["equals"] = JsonSerializer.SerializeToElement("!help"),
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "notify",
                    Type = WorkflowNodeTypes.Notify,
                    Config = new Dictionary<string, JsonElement>
                    {
                        ["message"] = JsonSerializer.SerializeToElement("must-not-run"),
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "end",
                    Type = WorkflowNodeTypes.End,
                },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-flow-start",
                    Source = "message-start",
                    SourceHandle = "out-flow",
                    Target = "condition",
                    TargetHandle = "in-flow",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-data-message",
                    Source = "message-start",
                    SourceHandle = "out-message-text",
                    Target = "condition",
                    TargetHandle = "in-field",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-true",
                    Source = "condition",
                    SourceHandle = "out-true",
                    Target = "end",
                    TargetHandle = "in-flow",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-false",
                    Source = "condition",
                    SourceHandle = "out-false",
                    Target = "notify",
                    TargetHandle = "in-flow",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-notify-next",
                    Source = "notify",
                    SourceHandle = "out-next",
                    Target = "end",
                    TargetHandle = "in-flow",
                },
            ],
            Ui = new WorkflowDraftUiModel
            {
                EntryNodeId = "message-start",
            },
        };

        var definition = new WorkflowDefinitionEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            DraftJson = JsonSerializer.Serialize(draft, WorkflowExecutionEngine.JsonOptions()),
            PublishedJson = JsonSerializer.Serialize(draft, WorkflowExecutionEngine.JsonOptions()),
            Status = "published",
            PublishedVersion = 1,
            MaxSteps = draft.MaxSteps,
            MaxDurationSeconds = draft.MaxDurationSeconds,
            MaxRetries = draft.MaxRetries,
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var execution = new WorkflowExecutionEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            WorkflowDefinitionId = definition.Id,
            TriggerEventId = triggerEvent.Id,
            SourceOrderId = triggerEvent.SourceOrderId,
            WorkflowVersion = 1,
            Status = "running",
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        var engine = new WorkflowExecutionEngine(
            new WorkflowNodeExecutorRegistry(new IWorkflowNodeExecutor[]
            {
                new MessageStartNodeExecutor(),
                new ConditionNodeExecutor(),
                new NotifyNodeExecutor(),
                new EndNodeExecutor(),
            }),
            NullLogger<WorkflowExecutionEngine>.Instance);

        var output = await engine.ExecuteAsync(
            dbContext,
            execution,
            definition,
            triggerEvent,
            CancellationToken.None);

        var executedNodeIds = await dbContext.WorkflowExecutionSteps
            .AsNoTracking()
            .OrderBy(step => step.StepIndex)
            .Select(step => step.NodeId)
            .ToListAsync();

        Assert.Equal(["message-start", "condition", "end"], executedNodeIds);
        Assert.Equal("!help", output["message.text"]?.ToString());
        Assert.False(output.ContainsKey("notify.message"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WorkflowExecutionEngine_ConditionFalse_DoesNotFallbackToTrueBranch()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"workflow-engine-condition-false-tests-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new CoreDbContext(options);

        var projectId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var triggerEvent = new WorkflowTriggerEventEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            Source = "message-webhook",
            SourceOrderId = "order-msg-condition-false",
            PayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["platform"] = "funpay",
                ["messageText"] = "обычное сообщение",
            }),
            Status = "accepted",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var draft = new WorkflowDraftModel
        {
            Version = "v1",
            MaxSteps = 16,
            MaxDurationSeconds = 60,
            MaxRetries = 1,
            Nodes =
            [
                new WorkflowNodeModel
                {
                    Id = "message-start",
                    Type = WorkflowNodeTypes.MessageStart,
                },
                new WorkflowNodeModel
                {
                    Id = "condition",
                    Type = WorkflowNodeTypes.Condition,
                    Config = new Dictionary<string, JsonElement>
                    {
                        ["field"] = JsonSerializer.SerializeToElement("message.text"),
                        ["equals"] = JsonSerializer.SerializeToElement("!help"),
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "notify",
                    Type = WorkflowNodeTypes.Notify,
                    Config = new Dictionary<string, JsonElement>
                    {
                        ["message"] = JsonSerializer.SerializeToElement("must-not-run"),
                    },
                },
                new WorkflowNodeModel
                {
                    Id = "end",
                    Type = WorkflowNodeTypes.End,
                },
            ],
            Edges =
            [
                new WorkflowEdgeModel
                {
                    Id = "edge-flow-start",
                    Source = "message-start",
                    SourceHandle = "out-flow",
                    Target = "condition",
                    TargetHandle = "in-flow",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-data-message",
                    Source = "message-start",
                    SourceHandle = "out-message-text",
                    Target = "condition",
                    TargetHandle = "in-field",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-only-true",
                    Source = "condition",
                    SourceHandle = "out-true",
                    Target = "notify",
                    TargetHandle = "in-flow",
                },
                new WorkflowEdgeModel
                {
                    Id = "edge-notify-next",
                    Source = "notify",
                    SourceHandle = "out-next",
                    Target = "end",
                    TargetHandle = "in-flow",
                },
            ],
            Ui = new WorkflowDraftUiModel
            {
                EntryNodeId = "message-start",
            },
        };

        var definition = new WorkflowDefinitionEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            DraftJson = JsonSerializer.Serialize(draft, WorkflowExecutionEngine.JsonOptions()),
            PublishedJson = JsonSerializer.Serialize(draft, WorkflowExecutionEngine.JsonOptions()),
            Status = "published",
            PublishedVersion = 1,
            MaxSteps = draft.MaxSteps,
            MaxDurationSeconds = draft.MaxDurationSeconds,
            MaxRetries = draft.MaxRetries,
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var execution = new WorkflowExecutionEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OfferId = offerId,
            WorkflowDefinitionId = definition.Id,
            TriggerEventId = triggerEvent.Id,
            SourceOrderId = triggerEvent.SourceOrderId,
            WorkflowVersion = 1,
            Status = "running",
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        var engine = new WorkflowExecutionEngine(
            new WorkflowNodeExecutorRegistry(new IWorkflowNodeExecutor[]
            {
                new MessageStartNodeExecutor(),
                new ConditionNodeExecutor(),
                new NotifyNodeExecutor(),
                new EndNodeExecutor(),
            }),
            NullLogger<WorkflowExecutionEngine>.Instance);

        var output = await engine.ExecuteAsync(
            dbContext,
            execution,
            definition,
            triggerEvent,
            CancellationToken.None);

        var executedNodeIds = await dbContext.WorkflowExecutionSteps
            .AsNoTracking()
            .OrderBy(step => step.StepIndex)
            .Select(step => step.NodeId)
            .ToListAsync();

        Assert.Equal(["message-start", "condition"], executedNodeIds);
        Assert.Equal("обычное сообщение", output["message.text"]?.ToString());
        Assert.False(output.ContainsKey("notify.message"));
    }
}
