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
            ? BuildAcceptanceCriteria(GetMissionObjective(context.Mission), context.Mission.SelectedRepository, context.Mission.SelectedSprint, context.Mission.SelectedWorkItem, context.Knowledge)
            : new List<string>();

        ExternalTaskDraft? externalTaskDraft = null;
        if (Role == AgentRole.Analyst && context.Mission.SelectedWorkItem is null)
        {
            var criteriaBody = acceptanceCriteria.Count == 0 ? "- Review mission manually" : string.Join(Environment.NewLine, acceptanceCriteria.Select(item => $"- {item}"));
            externalTaskDraft = new ExternalTaskDraft(
                $"[Analyst] {context.Mission.Title}",
                $"Mission prompt:{Environment.NewLine}{GetMissionObjective(context.Mission)}{Environment.NewLine}{Environment.NewLine}Repository:{Environment.NewLine}{context.Mission.SelectedRepository?.FullName ?? "Not selected"}{Environment.NewLine}{Environment.NewLine}Sprint:{Environment.NewLine}{context.Mission.SelectedSprint?.Title ?? "Not selected"}{Environment.NewLine}{Environment.NewLine}Acceptance criteria:{Environment.NewLine}{criteriaBody}{Environment.NewLine}{Environment.NewLine}Analysis:{Environment.NewLine}{summary}",
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
            {GetMissionObjective(context.Mission)}

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

    private static string GetMissionObjective(Mission mission)
    {
        return string.IsNullOrWhiteSpace(mission.Objective) ? mission.Prompt : mission.Objective;
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
    private readonly IAgentRuntimeCatalogStore _catalogStore;
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
        IAgentRuntimeCatalogStore catalogStore,
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
        _catalogStore = catalogStore;
        _patchPolicy = patchPolicy;
        _externalTaskSink = externalTaskSink;
        _executors = executors.ToDictionary(item => item.Role);
        _timeProvider = timeProvider;
        _modelOptions = modelOptions.Value;
        _logger = logger;
    }

    public ChannelReader<Guid> MissionQueue => _queue.Reader;

    public int QueueDepth => Volatile.Read(ref _queueDepth);

    public Task<Mission> CreateMissionAsync(CreateMissionRequest request, CancellationToken cancellationToken)
    {
        return CreateRunAsync(new CreateRunRequest
        {
            Title = request.Title,
            Objective = request.Prompt,
            SelectedRepository = request.SelectedRepository,
            SelectedSprint = request.SelectedSprint,
            SelectedWorkItem = request.SelectedWorkItem,
            SwarmTemplate = SwarmTemplate.Hierarchical,
            AutoCreatePullRequest = request.AutoCreatePullRequest
        }, cancellationToken);
    }

    public async Task<Mission> CreateRunAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        await _progressLogStore.InitializeAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var objective = request.Objective?.Trim() ?? string.Empty;
        var mission = new Mission
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? BuildFallbackTitle(objective) : request.Title.Trim(),
            Prompt = objective,
            Objective = objective,
            SwarmTemplate = request.SwarmTemplate,
            Status = MissionStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentPhase = "Queued",
            IsArchived = false,
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
        await PublishActivityAsync(mission.Id, ActivityEventType.MissionCreated, null, $"Run '{mission.Title}' queued.", GetMissionObjective(mission), cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.QueueStatusChanged, AgentRole.Manager, $"Logical queue depth is {depth}.", "Mission accepted by orchestrator.", cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "queued", "Mission accepted by orchestrator.", new Dictionary<string, string>
        {
            ["queueDepth"] = depth.ToString(),
            ["repository"] = mission.SelectedRepository?.FullName ?? string.Empty,
            ["sprint"] = mission.SelectedSprint?.Title ?? string.Empty,
            ["task"] = mission.SelectedWorkItem?.Title ?? string.Empty,
            ["swarmTemplate"] = mission.SwarmTemplate.ToString()
        }, cancellationToken);
        await _queue.Writer.WriteAsync(mission.Id, cancellationToken);
        return mission;
    }

    public async Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        return await _repository.GetMissionAsync(missionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Mission>> ListRunsAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        return await _repository.ListMissionsAsync(40, includeArchived: false, cancellationToken);
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

    public async Task<OverviewSnapshot> GetOverviewAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        var activeRun = await _repository.GetActiveMissionAsync(cancellationToken);
        var recentRuns = await _repository.ListMissionsAsync(12, includeArchived: false, cancellationToken);
        if (activeRun is not null)
        {
            recentRuns = recentRuns.Where(item => item.Id != activeRun.Id).ToList();
        }

        return new OverviewSnapshot
        {
            ActiveRun = activeRun,
            RecentRuns = recentRuns.ToList(),
            System = new OverviewSystemSnapshot
            {
                LogicalQueueDepth = QueueDepth,
                ChatModel = _modelOptions.ChatModel,
                PhysicalWorkerCount = _modelOptions.PhysicalWorkerCount
            },
            Agents = activeRun?.Agents ?? BuildRoster(_timeProvider.GetUtcNow())
        };
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await _progressLogStore.InitializeAsync(cancellationToken);
        var overview = await GetOverviewAsync(cancellationToken);
        var mission = overview.ActiveRun;
        var activities = mission is null ? [] : (await _repository.GetActivitiesAsync(mission.Id, cancellationToken)).Reverse().ToList();
        var progress = mission is null ? [] : (await _progressLogStore.GetByMissionAsync(mission.Id, 30, cancellationToken)).ToList();

        return new DashboardSnapshot
        {
            ActiveMission = mission,
            Agents = overview.Agents,
            RecentActivities = activities.TakeLast(20).ToList(),
            RecentProgressLogs = progress,
            PendingPatchProposals = mission?.PatchProposals.Where(item => item.Status == PatchProposalStatus.PendingReview).ToList() ?? [],
            LogicalQueueDepth = overview.System.LogicalQueueDepth,
            ChatModel = overview.System.ChatModel,
            PhysicalWorkerCount = overview.System.PhysicalWorkerCount
        };
    }

    public async Task<Mission?> ArchiveRunAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        var mission = await _repository.GetMissionAsync(missionId, cancellationToken);
        if (mission is null)
        {
            return null;
        }

        mission.IsArchived = true;
        mission.ArchivedAt = _timeProvider.GetUtcNow();
        mission.UpdatedAt = mission.ArchivedAt.Value;
        if (mission.Status is MissionStatus.Queued or MissionStatus.Running or MissionStatus.AwaitingPatchApproval)
        {
            mission.Status = MissionStatus.Cancelled;
            mission.CancelledAt ??= mission.ArchivedAt;
            mission.CancelledReason ??= "Archived by operator.";
        }

        mission.CurrentPhase = "Archived";
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Waiting, "Archived by operator");
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.QueueStatusChanged, AgentRole.Manager, "Run archived.", mission.Title, cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "run-archived", "Run archived by operator.", null, cancellationToken);
        return mission;
    }

    public async Task<Mission?> CancelRunAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        var mission = await _repository.GetMissionAsync(missionId, cancellationToken);
        if (mission is null)
        {
            return null;
        }

        if (mission.Status is MissionStatus.Completed or MissionStatus.Failed or MissionStatus.Cancelled)
        {
            return mission;
        }

        mission.Status = MissionStatus.Cancelled;
        mission.CancelledAt = _timeProvider.GetUtcNow();
        mission.CancelledReason = "Cancelled by operator.";
        mission.CurrentPhase = "Cancelled";
        mission.UpdatedAt = mission.CancelledAt.Value;
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Waiting, "Cancelled by operator");
        await _repository.SaveMissionAsync(mission, cancellationToken);
        await PublishActivityAsync(mission.Id, ActivityEventType.QueueStatusChanged, AgentRole.Manager, "Run cancelled.", mission.Title, cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "run-cancelled", "Run cancelled by operator.", null, cancellationToken);
        return mission;
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

        if (!proposal.AlreadyApplied)
        {
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
        }
        else
        {
            await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-already-applied", "Patch diff was already present in the workspace.", null, cancellationToken);
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
        if (proposal.AlreadyApplied)
        {
            var revertResult = await _workspaceToolset.RevertPatchAsync(mission, proposal, cancellationToken);
            if (!revertResult.Success)
            {
                proposal.Status = PatchProposalStatus.Failed;
                proposal.ReviewNote = revertResult.StdErr;
                proposal.UpdatedAt = _timeProvider.GetUtcNow();
                mission.UpdatedAt = _timeProvider.GetUtcNow();
                mission.CurrentPhase = "Patch revert failed";
                await _repository.SaveMissionAsync(mission, cancellationToken);
                await PublishActivityAsync(mission.Id, ActivityEventType.PatchFailed, proposal.AuthorRole, proposal.Title, revertResult.StdErr, cancellationToken);
                await PublishProgressAsync(mission.Id, proposal.AuthorRole, "patch-revert-failed", revertResult.StdErr, null, cancellationToken);
                return proposal;
            }
        }

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
        if (mission is null || mission.IsArchived || mission.Status == MissionStatus.Cancelled)
        {
            return;
        }

        var runtimeCatalog = await _catalogStore.GetCatalogAsync(cancellationToken);
        var swarmPlan = BuildSwarmAssignments(mission.SwarmTemplate);
        ValidateRunCapabilities(runtimeCatalog, swarmPlan);

        mission.Status = MissionStatus.Running;
        mission.CurrentPhase = GetPhaseForRole(AgentRole.Analyst);
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        UpdateQueueDepth(mission, QueueDepth);
        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Delegating, "Analyzing requirements and delegating work");
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return;
        }
        await PublishActivityAsync(mission.Id, ActivityEventType.AgentStatusChanged, AgentRole.Manager, "Manager is delegating.", mission.CurrentPhase ?? string.Empty, cancellationToken);
        await PublishProgressAsync(mission.Id, AgentRole.Manager, "analysis", "Manager started analysis and delegation.", new Dictionary<string, string>
        {
            ["repository"] = mission.SelectedRepository?.FullName ?? string.Empty,
            ["sprint"] = mission.SelectedSprint?.Title ?? string.Empty,
            ["task"] = mission.SelectedWorkItem?.Title ?? string.Empty,
            ["swarmTemplate"] = mission.SwarmTemplate.ToString()
        }, cancellationToken);

        var workspace = await _workspaceToolset.CaptureSnapshotAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = workspace.RootPath;
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return;
        }
        var knowledge = await _memoryStore.SearchAsync(BuildSearchQuery(mission), 5, cancellationToken);
        var results = new Dictionary<AgentRole, AgentExecutionResult>();

        foreach (var assignment in swarmPlan)
        {
            mission.CurrentPhase = GetPhaseForRole(assignment.Role);
            mission.UpdatedAt = _timeProvider.GetUtcNow();
            if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
            {
                return;
            }

            var result = await RunAgentAsync(
                mission,
                assignment.Role,
                workspace,
                knowledge,
                BuildPreviousSummary(assignment.Role, results),
                assignment.DelegatedBy,
                runtimeCatalog,
                cancellationToken);

            if (!await CanContinueProcessingMissionAsync(mission, cancellationToken))
            {
                return;
            }

            results[assignment.Role] = result;
            if (mission.Steps.Count == 0 && assignment.Role == AgentRole.Analyst)
            {
                mission.Steps = BuildSteps(mission.SwarmTemplate, result.AcceptanceCriteria);
            }

            MarkStep(mission, assignment.Role, MissionStepStatus.Completed, result.Summary);

            if (assignment.Role == AgentRole.Analyst && result.ExternalTaskDraft is not null)
            {
                mission.ExternalTask = await _externalTaskSink.CreateTaskAsync(result.ExternalTaskDraft, cancellationToken);
                mission.UpdatedAt = _timeProvider.GetUtcNow();
                if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
                {
                    return;
                }
                await PublishActivityAsync(mission.Id, ActivityEventType.ExternalTaskCreated, AgentRole.Analyst, mission.ExternalTask.Title, mission.ExternalTask.Url ?? mission.ExternalTask.Status, cancellationToken);
                await PublishProgressAsync(mission.Id, AgentRole.Analyst, "external-task", mission.ExternalTask.Title, new Dictionary<string, string>
                {
                    ["status"] = mission.ExternalTask.Status,
                    ["url"] = mission.ExternalTask.Url ?? string.Empty
                }, cancellationToken);
            }
        }

        SetAgentState(mission, AgentRole.Manager, AgentRunStatus.Completed, mission.PatchProposals.Any(item => item.Status == PatchProposalStatus.PendingReview)
            ? "Awaiting operator patch review"
            : "Mission completed");
        FinalizeMissionIfResolved(mission);
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return;
        }
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

    private async Task<AgentExecutionResult> RunAgentAsync(Mission mission, AgentRole role, WorkspaceSnapshot workspace, IReadOnlyList<KnowledgeChunk> knowledge, string? previousSummary, AgentRole? delegatedBy, AgentRuntimeCatalog runtimeCatalog, CancellationToken cancellationToken)
    {
        var executor = _executors[role];
        if (delegatedBy is not null)
        {
            EnsureDelegationAllowed(runtimeCatalog, delegatedBy.Value, role);
            await PublishProgressAsync(mission.Id, delegatedBy, "delegation", $"{delegatedBy} delegated work to {role}.", new Dictionary<string, string>
            {
                ["fromRole"] = delegatedBy.Value.ToString(),
                ["toRole"] = role.ToString()
            }, cancellationToken);
        }

        SetAgentState(mission, role, GetWorkingStatus(role), GetWorkingLabel(role));
        MarkStep(mission, role, MissionStepStatus.InProgress, GetWorkingLabel(role));
        mission.UpdatedAt = _timeProvider.GetUtcNow();
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return BuildArchivedExecutionResult();
        }
        await PublishActivityAsync(mission.Id, ActivityEventType.AgentStatusChanged, role, $"{role} started.", GetWorkingLabel(role), cancellationToken);
        await PublishProgressAsync(mission.Id, role, "started", GetWorkingLabel(role), null, cancellationToken);

        var result = await executor.ExecuteAsync(new AgentExecutionContext(
            mission,
            workspace,
            knowledge,
            previousSummary,
            (update, ct) => PublishProgressAsync(mission.Id, role, update.Stage, update.Message, update.Metadata is null ? null : new Dictionary<string, string>(update.Metadata), ct)), cancellationToken);
        if (!await CanContinueProcessingMissionAsync(mission, cancellationToken))
        {
            return BuildArchivedExecutionResult();
        }

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
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return result;
        }
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
        if (!await TrySaveProcessingMissionAsync(mission, cancellationToken))
        {
            return;
        }

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
        if (mission is null || mission.IsArchived || mission.Status == MissionStatus.Cancelled)
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

    private async Task<bool> TrySaveProcessingMissionAsync(Mission mission, CancellationToken cancellationToken)
    {
        if (!await CanContinueProcessingMissionAsync(mission, cancellationToken))
        {
            return false;
        }

        await _repository.SaveMissionAsync(mission, cancellationToken);
        return true;
    }

    private async Task<bool> CanContinueProcessingMissionAsync(Mission mission, CancellationToken cancellationToken)
    {
        var latest = await _repository.GetMissionAsync(mission.Id, cancellationToken);
        if (latest is null)
        {
            return false;
        }

        if (!latest.IsArchived && latest.Status != MissionStatus.Cancelled)
        {
            return true;
        }

        mission.IsArchived = latest.IsArchived;
        mission.ArchivedAt = latest.ArchivedAt;
        mission.CancelledAt = latest.CancelledAt;
        mission.CancelledReason = latest.CancelledReason;
        mission.Status = latest.Status;
        mission.CurrentPhase = latest.CurrentPhase;
        return false;
    }

    private static AgentExecutionResult BuildArchivedExecutionResult()
    {
        return new AgentExecutionResult(
            "Run archived by operator.",
            [],
            [],
            new Dictionary<string, string>(),
            null);
    }

    private static List<MissionStep> BuildSteps(SwarmTemplate template, IReadOnlyList<string> acceptanceCriteria)
    {
        var criteria = acceptanceCriteria.Count == 0 ? "No explicit acceptance criteria extracted." : string.Join(" | ", acceptanceCriteria);
        var roles = BuildSwarmAssignments(template).Select(item => item.Role).ToArray();

        var steps = new List<MissionStep>();
        var stepIds = roles.ToDictionary(role => role, _ => Guid.NewGuid());
        for (var index = 0; index < roles.Length; index++)
        {
            var step = new MissionStep
            {
                Id = stepIds[roles[index]],
                Title = $"{roles[index]} workstream",
                Owner = roles[index],
                Order = index + 1,
                Summary = criteria,
                Dependencies = BuildDependencies(template, roles[index], stepIds)
            };
            steps.Add(step);
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
        if (mission.Status == MissionStatus.Cancelled)
        {
            mission.CurrentPhase = "Cancelled";
            return;
        }

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
        var builder = new StringBuilder(GetMissionObjective(mission));
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

    private static string GetMissionObjective(Mission mission)
    {
        return string.IsNullOrWhiteSpace(mission.Objective) ? mission.Prompt : mission.Objective;
    }

    private static IReadOnlyList<SwarmAssignment> BuildSwarmAssignments(SwarmTemplate template)
    {
        return template switch
        {
            SwarmTemplate.Sequential =>
            [
                new SwarmAssignment(AgentRole.Analyst, AgentRole.Manager),
                new SwarmAssignment(AgentRole.WebDev, AgentRole.Analyst),
                new SwarmAssignment(AgentRole.Frontend, AgentRole.WebDev),
                new SwarmAssignment(AgentRole.Backend, AgentRole.Frontend),
                new SwarmAssignment(AgentRole.Tester, AgentRole.Backend),
                new SwarmAssignment(AgentRole.PM, AgentRole.Tester),
                new SwarmAssignment(AgentRole.Support, AgentRole.PM)
            ],
            SwarmTemplate.ParallelReview =>
            [
                new SwarmAssignment(AgentRole.Analyst, AgentRole.Manager),
                new SwarmAssignment(AgentRole.WebDev, AgentRole.Manager),
                new SwarmAssignment(AgentRole.Frontend, AgentRole.WebDev),
                new SwarmAssignment(AgentRole.Backend, AgentRole.WebDev),
                new SwarmAssignment(AgentRole.Tester, AgentRole.Manager),
                new SwarmAssignment(AgentRole.PM, AgentRole.Tester),
                new SwarmAssignment(AgentRole.Support, AgentRole.PM)
            ],
            _ =>
            [
                new SwarmAssignment(AgentRole.Analyst, AgentRole.Manager),
                new SwarmAssignment(AgentRole.WebDev, AgentRole.Manager),
                new SwarmAssignment(AgentRole.Frontend, AgentRole.WebDev),
                new SwarmAssignment(AgentRole.Backend, AgentRole.WebDev),
                new SwarmAssignment(AgentRole.Tester, AgentRole.Manager),
                new SwarmAssignment(AgentRole.PM, AgentRole.Manager),
                new SwarmAssignment(AgentRole.Support, AgentRole.Manager)
            ]
        };
    }

    private static List<Guid> BuildDependencies(SwarmTemplate template, AgentRole role, IReadOnlyDictionary<AgentRole, Guid> stepIds)
    {
        if (template == SwarmTemplate.Sequential)
        {
            var roles = BuildSwarmAssignments(template).Select(item => item.Role).ToArray();
            var index = Array.IndexOf(roles, role);
            return index <= 0 ? [] : [stepIds[roles[index - 1]]];
        }

        return role switch
        {
            AgentRole.Analyst => [],
            AgentRole.WebDev => [stepIds[AgentRole.Analyst]],
            AgentRole.Frontend => [stepIds[AgentRole.WebDev]],
            AgentRole.Backend => [stepIds[AgentRole.WebDev]],
            AgentRole.Tester => [stepIds[AgentRole.Frontend], stepIds[AgentRole.Backend]],
            AgentRole.PM => [stepIds[AgentRole.Tester]],
            AgentRole.Support => [stepIds[AgentRole.PM]],
            _ => []
        };
    }

    private void ValidateRunCapabilities(AgentRuntimeCatalog runtimeCatalog, IReadOnlyList<SwarmAssignment> swarmPlan)
    {
        var toolIndex = runtimeCatalog.Tools.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in swarmPlan)
        {
            if (!_executors.ContainsKey(assignment.Role))
            {
                throw new InvalidOperationException($"No executor registered for role '{assignment.Role}'.");
            }

            var policy = runtimeCatalog.Policies.FirstOrDefault(item => item.Role == assignment.Role);
            if (policy is null)
            {
                throw new InvalidOperationException($"Runtime policy is missing for role '{assignment.Role}'.");
            }

            if (policy.ExecutionMode != AgentExecutionMode.ToolLoop)
            {
                continue;
            }

            if (policy.AllowedTools.Count == 0)
            {
                throw new InvalidOperationException($"{assignment.Role} is configured for ToolLoop but has no allowed tools.");
            }

            var missingTools = policy.AllowedTools
                .Where(toolName => !toolIndex.TryGetValue(toolName, out var tool) || !tool.Enabled)
                .ToList();
            if (missingTools.Count > 0)
            {
                throw new InvalidOperationException($"{assignment.Role} requires unavailable tools: {string.Join(", ", missingTools)}.");
            }

            if (assignment.Role is AgentRole.Frontend or AgentRole.Backend && policy.WritableRoots.Count == 0)
            {
                throw new InvalidOperationException($"{assignment.Role} is configured for ToolLoop but has no writable roots.");
            }
        }
    }

    private static void EnsureDelegationAllowed(AgentRuntimeCatalog runtimeCatalog, AgentRole fromRole, AgentRole toRole)
    {
        var policy = runtimeCatalog.Policies.FirstOrDefault(item => item.Role == fromRole);
        if (policy is null)
        {
            throw new InvalidOperationException($"Delegation policy is missing for role '{fromRole}'.");
        }

        if (!policy.AllowedDelegates.Contains(toRole))
        {
            throw new InvalidOperationException($"{fromRole} is not allowed to delegate to {toRole}.");
        }
    }

    private static string? BuildPreviousSummary(AgentRole role, IReadOnlyDictionary<AgentRole, AgentExecutionResult> results)
    {
        return role switch
        {
            AgentRole.Analyst => null,
            AgentRole.WebDev => results.TryGetValue(AgentRole.Analyst, out var analyst) ? analyst.Summary : null,
            AgentRole.Frontend or AgentRole.Backend => results.TryGetValue(AgentRole.WebDev, out var webDev) ? webDev.Summary : null,
            AgentRole.Tester => $"Frontend patch count: {GetPatchCount(results, AgentRole.Frontend)}, Backend patch count: {GetPatchCount(results, AgentRole.Backend)}",
            AgentRole.PM => results.TryGetValue(AgentRole.Tester, out var tester) ? tester.Summary : null,
            AgentRole.Support => results.TryGetValue(AgentRole.PM, out var pm) ? pm.Summary : null,
            _ => null
        };
    }

    private static int GetPatchCount(IReadOnlyDictionary<AgentRole, AgentExecutionResult> results, AgentRole role)
    {
        return results.TryGetValue(role, out var result) ? result.ProposedPatches.Count : 0;
    }

    private static string GetPhaseForRole(AgentRole role)
    {
        return role switch
        {
            AgentRole.Analyst => "Scope analysis",
            AgentRole.WebDev => "Architecture planning",
            AgentRole.Frontend => "Frontend implementation",
            AgentRole.Backend => "Backend implementation",
            AgentRole.Tester => "Validation",
            AgentRole.PM => "Operator summary",
            AgentRole.Support => "Operator handoff",
            _ => "Running"
        };
    }

    private sealed record SwarmAssignment(AgentRole Role, AgentRole? DelegatedBy);
}
