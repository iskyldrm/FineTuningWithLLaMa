using System.Text;
using System.Threading.Channels;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.Extensions.Options;

namespace Apex.AgentTeam.Api.Services;

public sealed class StructuredAgentExecutor : IAgentExecutor
{
    private readonly IModelGateway _modelGateway;
    private readonly TimeProvider _timeProvider;

    public StructuredAgentExecutor(AgentRole role, IModelGateway modelGateway, TimeProvider timeProvider)
    {
        Role = role;
        _modelGateway = modelGateway;
        _timeProvider = timeProvider;
    }

    public AgentRole Role { get; }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = new ModelPrompt(Role, BuildSystemPrompt(), BuildUserPrompt(context), 0.15);
        var completion = await _modelGateway.CompleteAsync(prompt, cancellationToken);
        var summary = completion.Text.Trim();

        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Role.ToString()] = summary
        };

        var acceptanceCriteria = Role == AgentRole.Analyst
            ? BuildAcceptanceCriteria(context.Mission.Prompt, context.Mission.SelectedRepository, context.Mission.SelectedSprint, context.Mission.SelectedWorkItem, context.Knowledge)
            : new List<string>();

        ExternalTaskDraft? externalTaskDraft = null;
        if (Role == AgentRole.Analyst && context.Mission.SelectedWorkItem is null)
        {
            var criteriaBody = acceptanceCriteria.Count == 0 ? "- Review mission manually" : string.Join(Environment.NewLine, acceptanceCriteria.Select(item => $"- {item}"));
            externalTaskDraft = new ExternalTaskDraft(
                $"[Analyst] {context.Mission.Title}",
                $"Mission prompt:{Environment.NewLine}{context.Mission.Prompt}{Environment.NewLine}{Environment.NewLine}Repository:{Environment.NewLine}{context.Mission.SelectedRepository?.FullName ?? "Not selected"}{Environment.NewLine}{Environment.NewLine}Sprint:{Environment.NewLine}{context.Mission.SelectedSprint?.Title ?? "Not selected"}{Environment.NewLine}{Environment.NewLine}Acceptance criteria:{Environment.NewLine}{criteriaBody}{Environment.NewLine}{Environment.NewLine}Analysis:{Environment.NewLine}{summary}",
                ["agent-team", "analyst", "apex"],
                context.Mission.SelectedRepository?.Owner,
                context.Mission.SelectedRepository?.Name);
        }

        var proposedPatches = new List<PatchProposal>();
        if (Role is AgentRole.Frontend or AgentRole.Backend)
        {
            proposedPatches.Add(BuildPatchProposal(context.Mission, summary));
        }

        return new AgentExecutionResult(summary, acceptanceCriteria, proposedPatches, artifacts, externalTaskDraft);
    }

    private string BuildSystemPrompt()
    {
        return Role switch
        {
            AgentRole.Analyst => "You are the Analyst agent. Extract scope, acceptance criteria, repo/sprint context, risks, and the smallest viable backlog.",
            AgentRole.WebDev => "You are the WebDev architect. Define API/UI contracts, integration touchpoints, and implementation sequence.",
            AgentRole.Frontend => "You are the Frontend engineer. Focus on UI behavior, state flow, and patch-ready diffs.",
            AgentRole.Backend => "You are the Backend engineer. Focus on API contracts, orchestration, persistence, and patch-ready diffs.",
            AgentRole.Tester => "You are the Tester. Evaluate risk, validation steps, and keep-or-revert outcomes.",
            AgentRole.PM => "You are the PM. Summarize progress, rollout state, and next milestones for the operator.",
            AgentRole.Support => "You are the Support agent. Convert engineering state into a calm end-user explanation.",
            _ => "You are an expert software agent."
        };
    }

    private string BuildUserPrompt(AgentExecutionContext context)
    {
        var knowledge = context.Knowledge.Count == 0
            ? "No knowledge hits found."
            : string.Join(Environment.NewLine, context.Knowledge.Take(4).Select(item => $"- {item.SourcePath}: {Truncate(item.Content, 220)}"));

        var files = context.Workspace.Files.Count == 0
            ? "No files discovered."
            : string.Join(", ", context.Workspace.Files.Take(18));

        var repoContext = context.Mission.SelectedRepository is null
            ? "No GitHub repository selected."
            : $"Selected repository: {context.Mission.SelectedRepository.FullName} (default branch: {context.Mission.SelectedRepository.DefaultBranch})";

        var sprintContext = context.Mission.SelectedSprint is null
            ? "No sprint selected."
            : $"Selected sprint: {context.Mission.SelectedSprint.Title} ({context.Mission.SelectedSprint.State})";
        var taskContext = context.Mission.SelectedWorkItem is null
            ? "No GitHub board task selected."
            : $"Selected task: {context.Mission.SelectedWorkItem.Title} [{context.Mission.SelectedWorkItem.ContentType}] Status={context.Mission.SelectedWorkItem.Status} Sprint={context.Mission.SelectedWorkItem.SprintTitle}{Environment.NewLine}Task details: {Truncate(context.Mission.SelectedWorkItem.Description, 420)}";

        return $"""
            Mission title: {context.Mission.Title}
            Mission prompt:
            {context.Mission.Prompt}

            Repository context:
            {repoContext}

            Sprint context:
            {sprintContext}

            Task context:
            {taskContext}

            Previous summary:
            {context.PreviousSummary ?? "None"}

            Knowledge hits:
            {knowledge}

            Workspace sample files:
            {files}

            Workspace root:
            {context.Workspace.RootPath}

            Reply with a concise, implementation-oriented response.
            """;
    }

    private List<string> BuildAcceptanceCriteria(string prompt, RepositoryRef? repository, SprintRef? sprint, GitHubBoardItemRef? workItem, IReadOnlyList<KnowledgeChunk> knowledge)
    {
        var items = prompt.Split(['.', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 12)
            .Take(4)
            .ToList();

        if (repository is not null)
        {
            items.Add($"Use {repository.FullName} as the GitHub target repository.");
        }

        if (sprint is not null)
        {
            items.Add($"Align implementation with sprint {sprint.Title}.");
        }

        if (workItem is not null)
        {
            items.Add($"Implement GitHub board item '{workItem.Title}' in status lane '{workItem.Status}'.");
            foreach (var subtask in workItem.Subtasks.Take(4))
            {
                items.Add($"Complete checklist item: {subtask}");
            }
        }

        foreach (var reference in knowledge.Take(2))
        {
            items.Add($"Use {reference.SourcePath} as reference for implementation consistency.");
        }

        if (items.Count == 0)
        {
            items.Add("Deliver a working mission pipeline with visible agent state and patch review.");
        }

        return items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private PatchProposal BuildPatchProposal(Mission mission, string summary)
    {
        var slug = mission.Id.ToString("N")[..8];
        var relativePath = $"agent-output-{Role.ToString().ToLowerInvariant()}-{slug}.md";
        var lines = new List<string>
        {
            $"# {Role} Proposal",
            string.Empty,
            $"Generated at {_timeProvider.GetUtcNow():O}",
            string.Empty,
            "## Mission",
            mission.Title,
            string.Empty,
            "## Repository",
            mission.SelectedRepository?.FullName ?? "Not selected",
            string.Empty,
            "## Sprint",
            mission.SelectedSprint?.Title ?? "Not selected",
            string.Empty,
            "## Summary",
            summary,
            string.Empty,
            "## Next Action",
            "Review this proposal and either apply it or replace it with a repo-specific diff."
        };

        var patchBody = string.Join("\n", lines.Select(line => $"+{line}"));
        var diff = $"diff --git a/{relativePath} b/{relativePath}\nnew file mode 100644\nindex 0000000..1111111\n--- /dev/null\n+++ b/{relativePath}\n@@ -0,0 +1,{lines.Count} @@\n{patchBody}\n";

        return new PatchProposal
        {
            Id = Guid.NewGuid(),
            MissionId = mission.Id,
            AuthorRole = Role,
            Title = $"{Role} proposal for {mission.Title}",
            Summary = Truncate(summary, 240),
            Status = PatchProposalStatus.PendingReview,
            TargetPaths = [relativePath],
            Diff = diff,
            CreatedAt = _timeProvider.GetUtcNow()
        };
    }

    private static string Truncate(string value, int limit)
    {
        return value.Length > limit ? value[..limit] : value;
    }
}

public sealed class MissionOrchestrator : BackgroundService, IOrchestrator
{
    private readonly IMissionRepository _repository;
    private readonly IActivityStream _activityStream;
    private readonly IProgressLogStore _progressLogStore;
    private readonly IProgressStream _progressStream;
    private readonly IMemoryStore _memoryStore;
    private readonly IWorkspaceToolset _workspaceToolset;
    private readonly IGitHubCatalog _gitHubCatalog;
    private readonly IPatchPolicy _patchPolicy;
    private readonly IExternalTaskSink _externalTaskSink;
    private readonly IReadOnlyDictionary<AgentRole, IAgentExecutor> _executors;
    private readonly TimeProvider _timeProvider;
    private readonly ModelOptions _modelOptions;
    private readonly ILogger<MissionOrchestrator> _logger;
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private int _queueDepth;

    public MissionOrchestrator(
        IMissionRepository repository,
        IActivityStream activityStream,
        IProgressLogStore progressLogStore,
        IProgressStream progressStream,
        IMemoryStore memoryStore,
        IWorkspaceToolset workspaceToolset,
        IGitHubCatalog gitHubCatalog,
        IPatchPolicy patchPolicy,
        IExternalTaskSink externalTaskSink,
        IEnumerable<IAgentExecutor> executors,
        TimeProvider timeProvider,
        IOptions<ModelOptions> modelOptions,
        ILogger<MissionOrchestrator> logger)
    {
        _repository = repository;
        _activityStream = activityStream;
        _progressLogStore = progressLogStore;
        _progressStream = progressStream;
        _memoryStore = memoryStore;
        _workspaceToolset = workspaceToolset;
        _gitHubCatalog = gitHubCatalog;
        _patchPolicy = patchPolicy;
        _externalTaskSink = externalTaskSink;
        _executors = executors.ToDictionary(item => item.Role);
        _timeProvider = timeProvider;
        _modelOptions = modelOptions.Value;
        _logger = logger;
    }

    public ChannelReader<Guid> MissionQueue => _queue.Reader;

    public int QueueDepth => Volatile.Read(ref _queueDepth);

    public async Task<Mission> CreateMissionAsync(CreateMissionRequest request, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        await _progressLogStore.InitializeAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var mission = new Mission
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? BuildFallbackTitle(request.Prompt) : request.Title.Trim(),
            Prompt = request.Prompt.Trim(),
            Status = MissionStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentPhase = "Queued",
            SelectedRepository = request.SelectedRepository,
            SelectedSprint = request.SelectedSprint,
            SelectedWorkItem = request.SelectedWorkItem,
            AutoCreatePullRequest = request.AutoCreatePullRequest,
            Agents = BuildRoster(now)
        };

        var depth = Interlocked.Increment(ref _queueDepth);
        UpdateQueueDepth(mission, depth);
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Delegating, $"Queued with depth {depth}");

        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.MissionCreated, null, $"Mission '{mission.Title}' queued.", mission.Prompt, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.QueueStatusChanged, AgentRole.Manager, $"Logical queue depth is {depth}.", "Mission accepted by orchestrator.", cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "queued", "Mission accepted by orchestrator.", new Dictionary<string, string>
        {
            ["queueDepth"] = depth.ToString(),
            ["repository"] = mission.SelectedRepository?.FullName ?? string.Empty,
            ["sprint"] = mission.SelectedSprint?.Title ?? string.Empty,
            ["task"] = mission.SelectedWorkItem?.Title ?? string.Empty
        }, cancellationToken);
        await _queue.Writer.WriteAsync(mission.Id, cancellationToken);
        return mission;
    }

    public async Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        return await _repository.GetMissionAsync(missionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityEvent>> GetActivitiesAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        return await _repository.GetActivitiesAsync(missionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProgressLog>> GetProgressLogsAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _progressLogStore.InitializeAsync(cancellationToken);
        return await _progressLogStore.GetByMissionAsync(missionId, 120, cancellationToken);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        await _progressLogStore.InitializeAsync(cancellationToken);
        var mission = await _repository.GetLatestMissionAsync(cancellationToken);
        var agents = mission?.Agents ?? BuildRoster(_timeProvider.GetUtcNow());
        var activities = mission is null ? [] : (await _repository.GetActivitiesAsync(mission.Id, cancellationToken)).Reverse().ToList();
        var progress = mission is null ? [] : (await _progressLogStore.GetByMissionAsync(mission.Id, 30, cancellationToken)).ToList();

        return new DashboardSnapshot
        {
            ActiveMission = mission,
            Agents = agents,
            RecentActivities = activities.TakeLast(20).ToList(),
            RecentProgressLogs = progress,
            PendingPatchProposals = mission?.PatchProposals.Where(item => item.Status == PatchProposalStatus.PendingReview).ToList() ?? [],
            LogicalQueueDepth = QueueDepth,
            ChatModel = _modelOptions.ChatModel,
            PhysicalWorkerCount = _modelOptions.PhysicalWorkerCount
        };
    }

    public async Task<PatchProposal?> ApprovePatchAsync(Guid proposalId, PatchDecisionRequest request, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        var found = await _repository.FindPatchProposalAsync(proposalId, cancellationToken);
        if (found is null)
        {
            return null;
        }

        var mission = found.Value.Mission;
        var proposal = found.Value.Proposal;
        var now = _timeProvider.GetUtcNow();
        proposal.Status = PatchProposalStatus.Approved;
        proposal.ReviewNote = request.ReviewNote;
        proposal.UpdatedAt = now;
        mission.UpdatedAt = now;
        mission.CurrentPhase = "Applying patch";
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.PatchApproved, proposal.AuthorRole, proposal.Title, request.ReviewNote ?? "Approved by operator.", cancellationToken);
        await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-approved", proposal.Title, new Dictionary<string, string>
        {
            ["status"] = PatchProposalStatus.Approved.ToString()
        }, cancellationToken);

        var decision = _patchPolicy.Evaluate(proposal);
        if (!decision.IsAllowed)
        {
            proposal.Status = PatchProposalStatus.Failed;
            proposal.ReviewNote = decision.Reason;
            proposal.UpdatedAt = _timeProvider.GetUtcNow();
            mission.CurrentPhase = "Patch blocked";
            mission.UpdatedAt = _timeProvider.GetUtcNow();
            await _repository.SaveMissionAsync(mission, cancellationToken);
            await PublishActivityAsync(mission.Id, ActivityEventType.PatchFailed, proposal.AuthorRole, proposal.Title, decision.Reason, cancellationToken);
            await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-blocked", decision.Reason, null, cancellationToken);
            return proposal;
        }

        var applyResult = await _workspaceToolset.ApplyPatchAsync(mission, proposal, cancellationToken);
        if (!applyResult.Success)
        {
            proposal.Status = PatchProposalStatus.Failed;
            proposal.ReviewNote = applyResult.StdErr;
            proposal.UpdatedAt = _timeProvider.GetUtcNow();
            mission.CurrentPhase = "Patch apply failed";
            mission.UpdatedAt = _timeProvider.GetUtcNow();
            await _repository.SaveMissionAsync(mission, cancellationToken);
            await PublishActivityAsync(mission.Id, ActivityEventType.PatchFailed, proposal.AuthorRole, proposal.Title, applyResult.StdErr, cancellationToken);
            await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-apply-failed", applyResult.StdErr, null, cancellationToken);
            return proposal;
        }

        var validation = await _workspaceToolset.RunValidationAsync(mission, cancellationToken);
        if (!validation.Success)
        {
            await _workspaceToolset.RevertPatchAsync(mission, proposal, cancellationToken);
            proposal.Status = PatchProposalStatus.Failed;
            proposal.ReviewNote = validation.Output;
            proposal.UpdatedAt = _timeProvider.GetUtcNow();
            mission.CurrentPhase = "Validation failed";
            mission.UpdatedAt = _timeProvider.GetUtcNow();
            await _repository.SaveMissionAsync(mission, cancellationToken);
            await PublishActivityAsync(mission.Id, ActivityEventType.PatchFailed, proposal.AuthorRole, proposal.Title, validation.Output, cancellationToken);
            await PublishProgressAsync(mission.Id, proposal.AuthorRole, "validation-failed", validation.Output, null, cancellationToken);
            return proposal;
        }

        proposal.Status = PatchProposalStatus.Applied;
        proposal.UpdatedAt = _timeProvider.GetUtcNow();
        mission.CurrentPhase = "Patch applied";
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        FinalizeMissionIfResolved(mission);
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.PatchApplied, proposal.AuthorRole, proposal.Title, validation.Output, cancellationToken);
        await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-applied", validation.Output, new Dictionary<string, string>
        {
            ["status"] = PatchProposalStatus.Applied.ToString()
        }, cancellationToken);
        if (mission.Status == MissionStatus.Completed)
        {
            await PublishActivityAsync(mission.Id, ActivityEventType.MissionCompleted, AgentRole.Manager, mission.Title, "All pending patches were resolved.", cancellationToken);
            await PublishProgressAsync(mission.Id, AgentRole.Manager, "mission-completed", "All pending patches were resolved.", null, cancellationToken);
            await TryCreatePullRequestAsync(mission, cancellationToken);
        }

        return proposal;
    }

    public async Task<PatchProposal?> RejectPatchAsync(Guid proposalId, PatchDecisionRequest request, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        var found = await _repository.FindPatchProposalAsync(proposalId, cancellationToken);
        if (found is null)
        {
            return null;
        }

        var mission = found.Value.Mission;
        var proposal = found.Value.Proposal;
        proposal.Status = PatchProposalStatus.Rejected;
        proposal.ReviewNote = request.ReviewNote ?? "Rejected by operator.";
        proposal.UpdatedAt = _timeProvider.GetUtcNow();
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        mission.CurrentPhase = "Patch rejected";
        FinalizeMissionIfResolved(mission);
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.PatchRejected, proposal.AuthorRole, proposal.Title, proposal.ReviewNote, cancellationToken);
        await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-rejected", proposal.ReviewNote ?? string.Empty, new Dictionary<string, string>
        {
            ["status"] = PatchProposalStatus.Rejected.ToString()
        }, cancellationToken);
        if (mission.Status == MissionStatus.Completed)
        {
            await PublishActivityAsync(mission.Id, ActivityEventType.MissionCompleted, AgentRole.Manager, mission.Title, "Mission closed after patch decisions.", cancellationToken);
            await PublishProgressAsync(mission.Id, AgentRole.Manager, "mission-completed", "Mission closed after patch decisions.", null, cancellationToken);
            await TryCreatePullRequestAsync(mission, cancellationToken);
        }

        return proposal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _repository.InitializeAsync(stoppingToken);
        await _progressLogStore.InitializeAsync(stoppingToken);

        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_queue.Reader.TryRead(out var missionId))
            {
                Interlocked.Decrement(ref _queueDepth);
                try
                {
                    await ProcessMissionAsync(missionId, stoppingToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Mission {MissionId} failed in background processing.", missionId);
                    await MarkMissionFailedAsync(missionId, exception, stoppingToken);
                }
            }
        }
    }

    private async Task ProcessMissionAsync(Guid missionId, CancellationToken cancellationToken)
    {
        var mission = await _repository.GetMissionAsync(missionId, cancellationToken);
        if (mission is null)
        {
            return;
        }

        mission.Status = MissionStatus.Running;
        mission.CurrentPhase = "Analysis";
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        UpdateQueueDepth(mission, QueueDepth);
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Delegating, "Analyzing requirements and delegating work");
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.AgentStatusChanged, AgentRole.Manager, "Manager is delegating.", mission.CurrentPhase ?? string.Empty, cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "analysis", "Manager started analysis and delegation.", new Dictionary<string, string>
        {
            ["repository"] = mission.SelectedRepository?.FullName ?? string.Empty,
            ["sprint"] = mission.SelectedSprint?.Title ?? string.Empty,
            ["task"] = mission.SelectedWorkItem?.Title ?? string.Empty
        }, cancellationToken);

        var workspace = await _workspaceToolset.CaptureSnapshotAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = workspace.RootPath;
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        await _repository.SaveMissionAsync(mission, cancellationToken);
        var knowledge = await _memoryStore.SearchAsync(BuildSearchQuery(mission), 5, cancellationToken);

        var analyst = await RunAgentAsync(mission, AgentRole.Analyst, workspace, knowledge, null, AgentRole.Manager, cancellationToken);
        if (mission.Steps.Count == 0)
        {
            mission.Steps = BuildSteps(analyst.AcceptanceCriteria);
            MarkStep(mission, AgentRole.Analyst, MissionStepStatus.Completed, analyst.Summary);
        }

        if (analyst.ExternalTaskDraft is not null)
        {
            mission.ExternalTask = await _externalTaskSink.CreateTaskAsync(analyst.ExternalTaskDraft, cancellationToken);
            mission.UpdatedAt = _timeProvider.GetUtcNow();
            await _repository.SaveMissionAsync(mission, cancellationToken);
            await PublishActivityAsync(mission.Id, ActivityEventType.ExternalTaskCreated, AgentRole.Analyst, mission.ExternalTask.Title, mission.ExternalTask.Url ?? mission.ExternalTask.Status, cancellationToken);
            await PublishProgressAsync(mission.Id, AgentRole.Analyst, "external-task", mission.ExternalTask.Title, new Dictionary<string, string>
            {
                ["status"] = mission.ExternalTask.Status,
                ["url"] = mission.ExternalTask.Url ?? string.Empty
            }, cancellationToken);
        }

        var webDev = await RunAgentAsync(mission, AgentRole.WebDev, workspace, knowledge, analyst.Summary, AgentRole.Manager, cancellationToken);
        MarkStep(mission, AgentRole.WebDev, MissionStepStatus.Completed, webDev.Summary);
        await _repository.SaveMissionAsync(mission, cancellationToken);

        var frontend = await RunAgentAsync(mission, AgentRole.Frontend, workspace, knowledge, webDev.Summary, AgentRole.WebDev, cancellationToken);
        MarkStep(mission, AgentRole.Frontend, MissionStepStatus.Completed, frontend.Summary);
        await _repository.SaveMissionAsync(mission, cancellationToken);

        var backend = await RunAgentAsync(mission, AgentRole.Backend, workspace, knowledge, webDev.Summary, AgentRole.WebDev, cancellationToken);
        MarkStep(mission, AgentRole.Backend, MissionStepStatus.Completed, backend.Summary);
        await _repository.SaveMissionAsync(mission, cancellationToken);

        var testerSummary = $"Frontend patch count: {frontend.ProposedPatches.Count}, Backend patch count: {backend.ProposedPatches.Count}";
        var tester = await RunAgentAsync(mission, AgentRole.Tester, workspace, knowledge, testerSummary, AgentRole.Backend, cancellationToken);
        MarkStep(mission, AgentRole.Tester, MissionStepStatus.Completed, tester.Summary);

        var pm = await RunAgentAsync(mission, AgentRole.PM, workspace, knowledge, tester.Summary, AgentRole.Tester, cancellationToken);
        MarkStep(mission, AgentRole.PM, MissionStepStatus.Completed, pm.Summary);

        var support = await RunAgentAsync(mission, AgentRole.Support, workspace, knowledge, pm.Summary, AgentRole.PM, cancellationToken);
        MarkStep(mission, AgentRole.Support, MissionStepStatus.Completed, support.Summary);

        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Completed, mission.PatchProposals.Any(item => item.Status == PatchProposalStatus.PendingReview)
            ? "Awaiting operator patch review"
            : "Mission completed");
        mission.Status = mission.PatchProposals.Any(item => item.Status == PatchProposalStatus.PendingReview)
            ? MissionStatus.AwaitingPatchApproval
            : MissionStatus.Completed;
        mission.CurrentPhase = mission.Status == MissionStatus.Completed ? "Completed" : "Awaiting patch approval";
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.MissionCompleted, AgentRole.Manager, mission.Title, mission.CurrentPhase ?? string.Empty, cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, mission.Status == MissionStatus.Completed ? "mission-completed" : "awaiting-patch-review", mission.CurrentPhase ?? string.Empty, new Dictionary<string, string>
        {
            ["pendingPatches"] = mission.PatchProposals.Count(item => item.Status == PatchProposalStatus.PendingReview).ToString()
        }, cancellationToken);
        if (mission.Status == MissionStatus.Completed)
        {
            await TryCreatePullRequestAsync(mission, cancellationToken);
        }
    }

    private async Task<AgentExecutionResult> RunAgentAsync(Mission mission, AgentRole role, WorkspaceSnapshot workspace, IReadOnlyList<KnowledgeChunk> knowledge, string? previousSummary, AgentRole? delegatedBy, CancellationToken cancellationToken)
    {
        var executor = _executors[role];
        if (delegatedBy is not null)
        {
            await PublishProgressAsync(mission.Id, delegatedBy, "delegation", $"{delegatedBy} delegated work to {role}.", new Dictionary<string, string>
            {
                ["fromRole"] = delegatedBy.Value.ToString(),
                ["toRole"] = role.ToString()
            }, cancellationToken);
        }

        SetAgentState(mission, role, GetWorkingStatus(role), GetWorkingLabel(role));
        MarkStep(mission, role, MissionStepStatus.InProgress, GetWorkingLabel(role));
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.AgentStatusChanged, role, $"{role} started.", GetWorkingLabel(role), cancellationToken);
        await PublishProgressAsync(mission.Id, role, "started", GetWorkingLabel(role), null, cancellationToken);

        var result = await executor.ExecuteAsync(new AgentExecutionContext(mission, workspace, knowledge, previousSummary), cancellationToken);
        foreach (var artifact in result.Artifacts)
        {
            mission.Artifacts[artifact.Key] = artifact.Value;
        }

        foreach (var patch in result.ProposedPatches)
        {
            mission.PatchProposals.Add(patch);
            await PublishActivityAsync(mission.Id, ActivityEventType.PatchProposed, role, patch.Title, patch.Summary, cancellationToken);
            await PublishProgressAsync(mission.Id, role, "patch-proposed", patch.Title, new Dictionary<string, string>
            {
                ["paths"] = string.Join(",", patch.TargetPaths)
            }, cancellationToken);
        }

        SetAgentState(mission, role, AgentRunStatus.Completed, Truncate(result.Summary, 120));
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.AgentOutput, role, $"{role} completed.", result.Summary, cancellationToken);
        await PublishProgressAsync(mission.Id, role, "completed", result.Summary, null, cancellationToken);
        return result;
    }

    private async Task PublishActivityAsync(Guid missionId, ActivityEventType type, AgentRole? role, string summary, string details, CancellationToken cancellationToken)
    {
        var activity = new ActivityEvent
        {
            Id = Guid.NewGuid(),
            MissionId = missionId,
            CreatedAt = _timeProvider.GetUtcNow(),
            EventType = type,
            AgentRole = role,
            Summary = summary,
            Details = details
        };

        await _repository.AppendActivityAsync(activity, cancellationToken);
        await _activityStream.PublishAsync(activity, cancellationToken);
    }

    private async Task PublishProgressAsync(Guid missionId, AgentRole? role, string stage, string message, Dictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        var progress = new ProgressLog
        {
            Id = Guid.NewGuid(),
            MissionId = missionId,
            Role = role,
            Stage = stage,
            Message = message,
            Metadata = metadata ?? [],
            CreatedAt = _timeProvider.GetUtcNow()
        };

        await _progressLogStore.AppendAsync(progress, cancellationToken);
        await _progressStream.PublishAsync(progress, cancellationToken);
    }

    private async Task TryCreatePullRequestAsync(Mission mission, CancellationToken cancellationToken)
    {
        if (!mission.AutoCreatePullRequest
            || mission.SelectedRepository is null
            || mission.PullRequest is not null
            || mission.PatchProposals.All(item => item.Status != PatchProposalStatus.Applied))
        {
            return;
        }

        var branchResult = await _workspaceToolset.PublishBranchAsync(mission, cancellationToken);
        if (!branchResult.Success)
        {
            await PublishProgressAsync(mission.Id, AgentRole.Manager, "pr-skipped", branchResult.Output, new Dictionary<string, string>
            {
                ["workspace"] = branchResult.WorkingDirectory
            }, cancellationToken);
            return;
        }

        mission.PullRequest = await _gitHubCatalog.CreatePullRequestAsync(mission, branchResult, cancellationToken);
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        await _repository.SaveMissionAsync(mission, cancellationToken);

        if (mission.PullRequest is not null)
        {
            await PublishActivityAsync(mission.Id, ActivityEventType.AgentOutput, AgentRole.Manager, "Pull request created.", mission.PullRequest.Url ?? mission.PullRequest.Status, cancellationToken);
            await PublishProgressAsync(mission.Id, AgentRole.Manager, "pull-request", mission.PullRequest.Title, new Dictionary<string, string>
            {
                ["branch"] = mission.PullRequest.HeadBranch,
                ["base"] = mission.PullRequest.BaseBranch,
                ["url"] = mission.PullRequest.Url ?? string.Empty
            }, cancellationToken);
        }
    }

    private async Task MarkMissionFailedAsync(Guid missionId, Exception exception, CancellationToken cancellationToken)
    {
        var mission = await _repository.GetMissionAsync(missionId, cancellationToken);
        if (mission is null)
        {
            return;
        }

        mission.Status = MissionStatus.Failed;
        mission.CurrentPhase = "Failed";
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Error, exception.Message);
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.MissionFailed, AgentRole.Manager, mission.Title, exception.ToString(), cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "mission-failed", exception.Message, null, cancellationToken);
    }

    private static List<MissionStep> BuildSteps(IReadOnlyList<string> acceptanceCriteria)
    {
        var criteria = acceptanceCriteria.Count == 0 ? "No explicit acceptance criteria extracted." : string.Join(" | ", acceptanceCriteria);
        var roles = new[]
        {
            AgentRole.Analyst,
            AgentRole.WebDev,
            AgentRole.Frontend,
            AgentRole.Backend,
            AgentRole.Tester,
            AgentRole.PM,
            AgentRole.Support
        };

        var steps = new List<MissionStep>();
        Guid? previous = null;
        for (var index = 0; index < roles.Length; index++)
        {
            var step = new MissionStep
            {
                Id = Guid.NewGuid(),
                Title = $"{roles[index]} workstream",
                Owner = roles[index],
                Order = index + 1,
                Summary = criteria,
                Dependencies = previous is null ? [] : [previous.Value]
            };
            steps.Add(step);
            previous = step.Id;
        }

        return steps;
    }

    private static List<AgentSnapshot> BuildRoster(DateTimeOffset now)
    {
        return Enum.GetValues<AgentRole>()
            .Select(role => new AgentSnapshot
            {
                Role = role,
                Status = AgentRunStatus.Idle,
                Label = role.ToString(),
                UpdatedAt = now,
                QueueDepth = 0
            })
            .ToList();
    }

    private static void FinalizeMissionIfResolved(Mission mission)
    {
        if (mission.PatchProposals.All(item => item.Status != PatchProposalStatus.PendingReview))
        {
            mission.Status = MissionStatus.Completed;
            mission.CurrentPhase = "Completed";
        }
        else
        {
            mission.Status = MissionStatus.AwaitingPatchApproval;
            mission.CurrentPhase = "Awaiting patch approval";
        }
    }

    private static void UpdateQueueDepth(Mission mission, int queueDepth)
    {
        foreach (var snapshot in mission.Agents)
        {
            snapshot.QueueDepth = queueDepth;
        }
    }

    private void SetAgentState(Mission mission, AgentRole role, AgentRunStatus status, string detail)
    {
        var snapshot = mission.Agents.First(item => item.Role == role);
        snapshot.Status = status;
        snapshot.Detail = detail;
        snapshot.UpdatedAt = _timeProvider.GetUtcNow();
    }

    private static void MarkStep(Mission mission, AgentRole role, MissionStepStatus status, string summary)
    {
        var step = mission.Steps.FirstOrDefault(item => item.Owner == role);
        if (step is null)
        {
            return;
        }

        step.Status = status;
        step.Summary = summary;
    }

    private static AgentRunStatus GetWorkingStatus(AgentRole role)
    {
        return role switch
        {
            AgentRole.Frontend or AgentRole.Backend => AgentRunStatus.Coding,
            AgentRole.Tester => AgentRunStatus.Reviewing,
            AgentRole.PM or AgentRole.Support or AgentRole.Analyst or AgentRole.WebDev => AgentRunStatus.Thinking,
            _ => AgentRunStatus.Thinking
        };
    }

    private static string GetWorkingLabel(AgentRole role)
    {
        return role switch
        {
            AgentRole.Analyst => "Analyzing scope and acceptance criteria",
            AgentRole.WebDev => "Designing contract and integration plan",
            AgentRole.Frontend => "Preparing UI patch proposal",
            AgentRole.Backend => "Preparing API patch proposal",
            AgentRole.Tester => "Running keep-or-revert review",
            AgentRole.PM => "Summarizing sprint state",
            AgentRole.Support => "Preparing customer-facing explanation",
            _ => "Working"
        };
    }

    private static string BuildFallbackTitle(string prompt)
    {
        var cleaned = prompt.Trim().Replace(Environment.NewLine, " ");
        return cleaned.Length <= 60 ? cleaned : cleaned[..60];
    }

    private static string BuildSearchQuery(Mission mission)
    {
        var builder = new StringBuilder(mission.Prompt);
        if (!string.IsNullOrWhiteSpace(mission.SelectedRepository?.FullName))
        {
            builder.Append(' ').Append(mission.SelectedRepository.FullName);
        }

        if (!string.IsNullOrWhiteSpace(mission.SelectedSprint?.Title))
        {
            builder.Append(' ').Append(mission.SelectedSprint.Title);
        }

        if (!string.IsNullOrWhiteSpace(mission.SelectedWorkItem?.Title))
        {
            builder.Append(' ').Append(mission.SelectedWorkItem.Title);
        }

        if (!string.IsNullOrWhiteSpace(mission.SelectedWorkItem?.Description))
        {
            builder.Append(' ').Append(mission.SelectedWorkItem.Description);
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int limit)
    {
        return value.Length > limit ? value[..limit] : value;
    }
}
