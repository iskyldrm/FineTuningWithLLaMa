namespace Apex.AgentTeam.Api.Models;

using System.Text.Json.Serialization;
using Apex.AgentTeam.Api.Infrastructure;

public enum AgentRole
{
    Manager,
    Analyst,
    WebDev,
    Frontend,
    Backend,
    Tester,
    PM,
    Support
}

public enum AgentRunStatus
{
    Idle,
    Thinking,
    Delegating,
    Coding,
    Reviewing,
    Waiting,
    Completed,
    Error
}

public enum MissionStatus
{
    Draft,
    Queued,
    Running,
    AwaitingPatchApproval,
    Completed,
    Failed
}

public enum MissionStepStatus
{
    Pending,
    InProgress,
    Completed,
    Blocked
}

public enum PatchProposalStatus
{
    PendingReview,
    Approved,
    Rejected,
    Applied,
    Failed
}

public enum AgentExecutionMode
{
    StructuredPrompt,
    ToolLoop
}

public enum AgentToolType
{
    ListFiles,
    ReadFile,
    WriteFile,
    SearchCode,
    RunTerminal,
    GitStatus,
    GitDiff,
    GitCommit,
    GitPush,
    CustomCommand
}

public enum ActivityEventType
{
    MissionCreated,
    AgentStatusChanged,
    AgentOutput,
    ExternalTaskCreated,
    PatchProposed,
    PatchApproved,
    PatchRejected,
    PatchApplied,
    PatchFailed,
    QueueStatusChanged,
    MissionCompleted,
    MissionFailed,
    KnowledgeIngested
}

public sealed class Mission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public MissionStatus Status { get; set; } = MissionStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CurrentPhase { get; set; }
    public RepositoryRef? SelectedRepository { get; set; }
    public SprintRef? SelectedSprint { get; set; }
    public GitHubBoardItemRef? SelectedWorkItem { get; set; }
    public ExternalTaskRef? ExternalTask { get; set; }
    public PullRequestRef? PullRequest { get; set; }
    public bool AutoCreatePullRequest { get; set; } = true;
    public string? WorkspaceRootPath { get; set; }
    public List<MissionStep> Steps { get; set; } = [];
    public List<PatchProposal> PatchProposals { get; set; } = [];
    public List<AgentSnapshot> Agents { get; set; } = [];
    public Dictionary<string, string> Artifacts { get; set; } = [];
}

public sealed class MissionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public AgentRole Owner { get; set; }
    public MissionStepStatus Status { get; set; } = MissionStepStatus.Pending;
    public int Order { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<Guid> Dependencies { get; set; } = [];
}

public sealed class PatchProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MissionId { get; set; }
    public AgentRole AuthorRole { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public PatchProposalStatus Status { get; set; } = PatchProposalStatus.PendingReview;
    public List<string> TargetPaths { get; set; } = [];
    public string Diff { get; set; } = string.Empty;
    public bool AlreadyApplied { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MissionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ActivityEventType EventType { get; set; }
    public AgentRole? AgentRole { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class KnowledgeChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public List<string> Links { get; set; } = [];
}

public sealed class RepositoryRef
{
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;
}

public sealed class SprintRef
{
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Number { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? DueOn { get; set; }
    public string? IterationId { get; set; }
    public string? ProjectId { get; set; }
    public int? ProjectNumber { get; set; }
    public string? ProjectTitle { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public int? DurationDays { get; set; }
}

public sealed class GitHubProjectRef
{
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string OwnerLogin { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public bool Closed { get; set; }
}

public sealed class GitHubBoardItemRef
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int ProjectNumber { get; set; }
    public string ProjectTitle { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string? IterationId { get; set; }
    public string SprintTitle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusOptionId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int? Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string RepositoryOwner { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string RepositoryFullName { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];
    public List<string> Assignees { get; set; } = [];
    public List<string> Subtasks { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDraft { get; set; }
}

public sealed class GitHubBoardColumn
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<GitHubBoardItemRef> Items { get; set; } = [];
}

public sealed class GitHubBoardSnapshot
{
    public RepositoryRef Repository { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public List<GitHubProjectRef> Projects { get; set; } = [];
    public List<SprintRef> Sprints { get; set; } = [];
    public List<GitHubBoardColumn> Columns { get; set; } = [];
    public List<GitHubBoardItemRef> Items { get; set; } = [];
}

public sealed class ProgressLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MissionId { get; set; }
    public AgentRole? Role { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExternalTaskRef
{
    public string Provider { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class PullRequestRef
{
    public string Provider { get; set; } = "github";
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Status { get; set; } = string.Empty;
    public string HeadBranch { get; set; } = string.Empty;
    public string BaseBranch { get; set; } = string.Empty;
}

public sealed class AgentSnapshot
{
    public AgentRole Role { get; set; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Idle;
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int QueueDepth { get; set; }
}

public sealed class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AgentToolType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Destructive { get; set; }
    public string? CommandTemplate { get; set; }
}

public sealed class AgentRolePolicy
{
    public AgentRole Role { get; set; }
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.StructuredPrompt;
    public List<string> AllowedTools { get; set; } = [];
    public List<string> WritableRoots { get; set; } = [];
    public int MaxSteps { get; set; } = 6;
}

public sealed class AgentRuntimeCatalog
{
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AgentToolDefinition> Tools { get; set; } = [];
    public List<AgentRolePolicy> Policies { get; set; } = [];
}

public sealed class ChatThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ChatUsage? Usage { get; set; }
}

public sealed class ChatUsage
{
    public int? PromptEvalCount { get; set; }
    public int? EvalCount { get; set; }
}

public sealed class ChatExchangeResult
{
    public ChatThread Thread { get; set; } = new();
    public ChatMessage UserMessage { get; set; } = new();
    public ChatMessage AssistantMessage { get; set; } = new();
}

public sealed class OllamaModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string ParameterSize { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; set; }
    public long Size { get; set; }
}

public sealed class DashboardSnapshot
{
    public Mission? ActiveMission { get; set; }
    public List<AgentSnapshot> Agents { get; set; } = [];
    public List<ActivityEvent> RecentActivities { get; set; } = [];
    public List<ProgressLog> RecentProgressLogs { get; set; } = [];
    public List<PatchProposal> PendingPatchProposals { get; set; } = [];
    public int LogicalQueueDepth { get; set; }
    public string ChatModel { get; set; } = string.Empty;
    public int PhysicalWorkerCount { get; set; }
}

public sealed class CreateMissionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public RepositoryRef? SelectedRepository { get; set; }
    public SprintRef? SelectedSprint { get; set; }
    public GitHubBoardItemRef? SelectedWorkItem { get; set; }
    public bool AutoCreatePullRequest { get; set; } = true;
}

public sealed class PatchDecisionRequest
{
    public string? ReviewNote { get; set; }
}

public sealed class CreateChatThreadRequest
{
    public string? Title { get; set; }
    public string? Model { get; set; }
}

public sealed class SendChatMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
}

public sealed class UpsertAgentToolRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AgentToolType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Destructive { get; set; }
    public string? CommandTemplate { get; set; }
}

public sealed class UpdateAgentRolePolicyRequest
{
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.StructuredPrompt;
    public List<string> AllowedTools { get; set; } = [];
    public List<string> WritableRoots { get; set; } = [];
    public int MaxSteps { get; set; } = 6;
}

