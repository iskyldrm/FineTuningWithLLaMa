using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Apex.AgentTeam.Api.Hubs;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Apex.AgentTeam.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
builder.Services.Configure<ModelOptions>(builder.Configuration.GetSection(ModelOptions.SectionName));
builder.Services.Configure<WorkspaceOptions>(builder.Configuration.GetSection(WorkspaceOptions.SectionName));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .SetIsOriginAllowed(_ => true));
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IMissionRepository, PostgresMissionRepository>();
builder.Services.AddSingleton<MongoTelemetryStore>();
builder.Services.AddSingleton<IProgressLogStore>(sp => sp.GetRequiredService<MongoTelemetryStore>());
builder.Services.AddSingleton<IChatStore>(sp => sp.GetRequiredService<MongoTelemetryStore>());
builder.Services.AddSingleton<SignalRRealtimeStream>();
builder.Services.AddSingleton<IActivityStream>(sp => sp.GetRequiredService<SignalRRealtimeStream>());
builder.Services.AddSingleton<IProgressStream>(sp => sp.GetRequiredService<SignalRRealtimeStream>());
builder.Services.AddSingleton<IMemoryStore, QdrantMemoryStore>();
builder.Services.AddSingleton<IWorkspaceToolset, GitWorkspaceToolset>();
builder.Services.AddSingleton<IPatchPolicy, UnifiedDiffPatchPolicy>();
builder.Services.AddSingleton<IExternalTaskSink, GitHubIssueSink>();
builder.Services.AddSingleton<IGitHubCatalog, GitHubCatalogService>();
builder.Services.AddSingleton<IModelGateway, OllamaModelGateway>();
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.Analyst, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.WebDev, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.Frontend, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.Backend, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.Tester, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.PM, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAgentExecutor>(sp => new StructuredAgentExecutor(AgentRole.Support, sp.GetRequiredService<IModelGateway>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<MissionOrchestrator>();
builder.Services.AddSingleton<IOrchestrator>(sp => sp.GetRequiredService<MissionOrchestrator>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MissionOrchestrator>());
builder.Services.AddHostedService<KnowledgeIngestionHostedService>();

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/api/dashboard"));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/dashboard", async (IOrchestrator orchestrator, CancellationToken cancellationToken) =>
    Results.Ok(await orchestrator.GetDashboardAsync(cancellationToken)));

app.MapGet("/api/github/repositories", async (IGitHubCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.ListRepositoriesAsync(cancellationToken)));

app.MapGet("/api/github/repositories/{owner}/{repo}/milestones", async (string owner, string repo, IGitHubCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.ListMilestonesAsync(owner, repo, cancellationToken)));

app.MapPost("/api/github/repositories/{owner}/{repo}/milestones/defaults", async (string owner, string repo, IGitHubCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.EnsureDefaultMilestonesAsync(owner, repo, cancellationToken)));

app.MapGet("/api/github/repositories/{owner}/{repo}/board", async (string owner, string repo, IGitHubCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.GetRepositoryBoardAsync(owner, repo, cancellationToken)));

app.MapGet("/api/ollama/models", async (IModelGateway modelGateway, CancellationToken cancellationToken) =>
    Results.Ok(await modelGateway.ListModelsAsync(cancellationToken)));

app.MapGet("/api/missions/{missionId:guid}", async (Guid missionId, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var mission = await orchestrator.GetMissionAsync(missionId, cancellationToken);
    return mission is null ? Results.NotFound() : Results.Ok(mission);
});

app.MapGet("/api/missions/{missionId:guid}/activities", async (Guid missionId, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
    Results.Ok(await orchestrator.GetActivitiesAsync(missionId, cancellationToken)));

app.MapGet("/api/missions/{missionId:guid}/progress", async (Guid missionId, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
    Results.Ok(await orchestrator.GetProgressLogsAsync(missionId, cancellationToken)));

app.MapPost("/api/missions", async (CreateMissionRequest request, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var mission = await orchestrator.CreateMissionAsync(request, cancellationToken);
    return Results.Created($"/api/missions/{mission.Id}", mission);
});

app.MapPost("/api/patches/{proposalId:guid}/approve", async (Guid proposalId, PatchDecisionRequest request, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var patch = await orchestrator.ApprovePatchAsync(proposalId, request, cancellationToken);
    return patch is null ? Results.NotFound() : Results.Ok(patch);
});

app.MapPost("/api/patches/{proposalId:guid}/reject", async (Guid proposalId, PatchDecisionRequest request, IOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var patch = await orchestrator.RejectPatchAsync(proposalId, request, cancellationToken);
    return patch is null ? Results.NotFound() : Results.Ok(patch);
});

app.MapGet("/api/chat/threads", async (IChatStore chatStore, CancellationToken cancellationToken) =>
{
    await chatStore.InitializeAsync(cancellationToken);
    return Results.Ok(await chatStore.ListThreadsAsync(cancellationToken));
});

app.MapPost("/api/chat/threads", async (CreateChatThreadRequest request, IChatStore chatStore, IOptions<ModelOptions> modelOptions, CancellationToken cancellationToken) =>
{
    await chatStore.InitializeAsync(cancellationToken);
    var now = DateTimeOffset.UtcNow;
    var thread = new ChatThread
    {
        Id = Guid.NewGuid(),
        Title = string.IsNullOrWhiteSpace(request.Title) ? "New chat" : request.Title.Trim(),
        Model = string.IsNullOrWhiteSpace(request.Model) ? modelOptions.Value.ChatModel : request.Model.Trim(),
        CreatedAt = now,
        UpdatedAt = now
    };

    await chatStore.CreateThreadAsync(thread, cancellationToken);
    return Results.Created($"/api/chat/threads/{thread.Id}", thread);
});

app.MapGet("/api/chat/threads/{threadId:guid}/messages", async (Guid threadId, IChatStore chatStore, CancellationToken cancellationToken) =>
{
    await chatStore.InitializeAsync(cancellationToken);
    var thread = await chatStore.GetThreadAsync(threadId, cancellationToken);
    return thread is null ? Results.NotFound() : Results.Ok(await chatStore.GetMessagesAsync(threadId, cancellationToken));
});

app.MapPost("/api/chat/threads/{threadId:guid}/messages", async (Guid threadId, SendChatMessageRequest request, IChatStore chatStore, IModelGateway modelGateway, IOptions<ModelOptions> modelOptions, CancellationToken cancellationToken) =>
{
    await chatStore.InitializeAsync(cancellationToken);
    var thread = await chatStore.GetThreadAsync(threadId, cancellationToken);
    if (thread is null)
    {
        return Results.NotFound();
    }

    var content = request.Content?.Trim() ?? string.Empty;
    if (content.Length == 0)
    {
        return Results.BadRequest(new { error = "Message content is required." });
    }

    var selectedModel = string.IsNullOrWhiteSpace(request.Model)
        ? (string.IsNullOrWhiteSpace(thread.Model) ? modelOptions.Value.ChatModel : thread.Model)
        : request.Model.Trim();

    var userMessage = new ChatMessage
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        Role = "user",
        Content = content,
        Model = selectedModel,
        CreatedAt = DateTimeOffset.UtcNow
    };

    await chatStore.AppendMessageAsync(userMessage, cancellationToken);
    var history = await chatStore.GetMessagesAsync(threadId, cancellationToken);
    var completion = await modelGateway.ChatAsync(selectedModel, history, cancellationToken);

    var assistantMessage = new ChatMessage
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        Role = "assistant",
        Content = completion.Content,
        Model = selectedModel,
        CreatedAt = DateTimeOffset.UtcNow,
        Usage = completion.Usage
    };

    await chatStore.AppendMessageAsync(assistantMessage, cancellationToken);

    thread.Model = selectedModel;
    thread.UpdatedAt = DateTimeOffset.UtcNow;
    if (string.IsNullOrWhiteSpace(thread.Title) || string.Equals(thread.Title, "New chat", StringComparison.OrdinalIgnoreCase))
    {
        thread.Title = content.Length > 42 ? $"{content[..42]}..." : content;
    }

    await chatStore.UpdateThreadAsync(thread, cancellationToken);
    return Results.Ok(new ChatExchangeResult
    {
        Thread = thread,
        UserMessage = userMessage,
        AssistantMessage = assistantMessage
    });
});

app.MapHub<ActivityHub>("/hubs/activity");

app.Run();
