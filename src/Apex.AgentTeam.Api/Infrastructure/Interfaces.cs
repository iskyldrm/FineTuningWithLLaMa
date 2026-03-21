using System.Threading.Channels;
using Apex.AgentTeam.Api.Models;

namespace Apex.AgentTeam.Api.Infrastructure;

public interface IModelGateway
{
    Task<ModelTextResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken);

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

    Task<ModelChatResponse> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}

public interface IOrchestrator
{
    ChannelReader<Guid> MissionQueue { get; }

    int QueueDepth { get; }

    Task<Mission> CreateMissionAsync(CreateMissionRequest request, CancellationToken cancellationToken);

    Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityEvent>> GetActivitiesAsync(Guid missionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProgressLog>> GetProgressLogsAsync(Guid missionId, CancellationToken cancellationToken);

    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken);

    Task<PatchProposal?> ApprovePatchAsync(Guid proposalId, PatchDecisionRequest request, CancellationToken cancellationToken);

    Task<PatchProposal?> RejectPatchAsync(Guid proposalId, PatchDecisionRequest request, CancellationToken cancellationToken);
}

public interface IAgentExecutor
{
    AgentRole Role { get; }

    Task<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken);
}

public interface IMemoryStore
{
    Task SeedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

public interface IPatchPolicy
{
    PatchPolicyDecision Evaluate(PatchProposal proposal);
}

public interface IWorkspaceToolset
{
    Task<WorkspaceSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken);

    Task<PatchApplyResult> ApplyPatchAsync(PatchProposal proposal, CancellationToken cancellationToken);

    Task<PatchApplyResult> RevertPatchAsync(PatchProposal proposal, CancellationToken cancellationToken);

    Task<TestRunResult> RunValidationAsync(CancellationToken cancellationToken);
}

public interface IExternalTaskSink
{
    Task<ExternalTaskRef> CreateTaskAsync(ExternalTaskDraft draft, CancellationToken cancellationToken);
}

public interface IGitHubCatalog
{
    Task<IReadOnlyList<RepositoryRef>> ListRepositoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SprintRef>> ListMilestonesAsync(string owner, string repository, CancellationToken cancellationToken);

    Task<IReadOnlyList<SprintRef>> EnsureDefaultMilestonesAsync(string owner, string repository, CancellationToken cancellationToken);
}

public interface IMissionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task SaveMissionAsync(Mission mission, CancellationToken cancellationToken);

    Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken);

    Task<Mission?> GetLatestMissionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityEvent>> GetActivitiesAsync(Guid missionId, CancellationToken cancellationToken);

    Task AppendActivityAsync(ActivityEvent activityEvent, CancellationToken cancellationToken);

    Task<(Mission Mission, PatchProposal Proposal)?> FindPatchProposalAsync(Guid proposalId, CancellationToken cancellationToken);
}

public interface IProgressLogStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task AppendAsync(ProgressLog progressLog, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProgressLog>> GetByMissionAsync(Guid missionId, int limit, CancellationToken cancellationToken);
}

public interface IChatStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatThread>> ListThreadsAsync(CancellationToken cancellationToken);

    Task<ChatThread?> GetThreadAsync(Guid threadId, CancellationToken cancellationToken);

    Task<ChatThread> CreateThreadAsync(ChatThread thread, CancellationToken cancellationToken);

    Task UpdateThreadAsync(ChatThread thread, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken cancellationToken);

    Task AppendMessageAsync(ChatMessage message, CancellationToken cancellationToken);
}

public interface IActivityStream
{
    Task PublishAsync(ActivityEvent activityEvent, CancellationToken cancellationToken);
}

public interface IProgressStream
{
    Task PublishAsync(ProgressLog progressLog, CancellationToken cancellationToken);
}

public sealed record ModelPrompt(AgentRole Role, string SystemPrompt, string UserPrompt, double Temperature);

public sealed record ModelTextResponse(string Text, bool UsedFallback);

public sealed record ModelChatResponse(string Content, ChatUsage? Usage, bool UsedFallback);

public sealed record AgentExecutionContext(Mission Mission, WorkspaceSnapshot Workspace, IReadOnlyList<KnowledgeChunk> Knowledge, string? PreviousSummary);

public sealed record AgentExecutionResult(string Summary, IReadOnlyList<string> AcceptanceCriteria, IReadOnlyList<PatchProposal> ProposedPatches, IReadOnlyDictionary<string, string> Artifacts, ExternalTaskDraft? ExternalTaskDraft);

public sealed record ExternalTaskDraft(string Title, string Body, IReadOnlyList<string> Labels, string? RepositoryOwner = null, string? RepositoryName = null);

public sealed record WorkspaceSnapshot(string RootPath, IReadOnlyList<string> Files, IReadOnlyDictionary<string, string> FilePreviews);

public sealed record PatchPolicyDecision(bool IsAllowed, string Reason);

public sealed record PatchApplyResult(bool Success, string StdOut, string StdErr);

public sealed record TestRunResult(bool Success, string Output);

