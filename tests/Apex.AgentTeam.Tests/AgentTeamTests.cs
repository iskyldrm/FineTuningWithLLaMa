using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Apex.AgentTeam.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Apex.AgentTeam.Tests;

public sealed class AgentTeamTests
{
    [Fact]
    public async Task FrontendExecutor_CreatesPendingReviewPatchProposal()
    {
        var executor = new StructuredAgentExecutor(AgentRole.Frontend, new FakeModelGateway("Frontend patch summary"), TimeProvider.System);
        var mission = BuildMission();
        var result = await executor.ExecuteAsync(new AgentExecutionContext(mission, BuildWorkspace(), [], null), CancellationToken.None);

        var patch = Assert.Single(result.ProposedPatches);
        Assert.Equal(PatchProposalStatus.PendingReview, patch.Status);
        Assert.Equal(AgentRole.Frontend, patch.AuthorRole);
        Assert.Contains("diff --git", patch.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalystExecutor_ProducesCriteriaAndExternalTaskDraft()
    {
        var executor = new StructuredAgentExecutor(AgentRole.Analyst, new FakeModelGateway("Analyst extracted scope and acceptance criteria."), TimeProvider.System);
        var mission = BuildMission();
        var knowledge = new List<KnowledgeChunk>
        {
            new() { SourcePath = "APEX.md", Title = "APEX", Content = "Role-based multi-agent orchestration with shared memory." }
        };

        var result = await executor.ExecuteAsync(new AgentExecutionContext(mission, BuildWorkspace(), knowledge, null), CancellationToken.None);

        Assert.NotEmpty(result.AcceptanceCriteria);
        Assert.NotNull(result.ExternalTaskDraft);
        Assert.Contains("Analyst", result.ExternalTaskDraft!.Title, StringComparison.Ordinal);
        Assert.Equal("local", result.ExternalTaskDraft.RepositoryOwner);
        Assert.Equal("apex", result.ExternalTaskDraft.RepositoryName);
    }

    [Fact]
    public void PatchPolicy_BlocksProtectedPaths()
    {
        var policy = new UnifiedDiffPatchPolicy();
        var proposal = new PatchProposal
        {
            AuthorRole = AgentRole.Backend,
            TargetPaths = [".git/config"],
            Diff = "diff --git a/.git/config b/.git/config"
        };

        var decision = policy.Evaluate(proposal);

        Assert.False(decision.IsAllowed);
        Assert.Contains("protected path", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_CreateMission_QueuesAndPersistsMission_WithRepoContext()
    {
        var repository = new InMemoryMissionRepository();
        var progressStore = new InMemoryProgressStore();
        var orchestrator = new MissionOrchestrator(
            repository,
            new NullActivityStream(),
            progressStore,
            new NullProgressStream(),
            new NullMemoryStore(),
            new NullWorkspaceToolset(),
            new NullGitHubCatalog(),
            new UnifiedDiffPatchPolicy(),
            new NullTaskSink(),
            [new StructuredAgentExecutor(AgentRole.Analyst, new FakeModelGateway("analysis"), TimeProvider.System)],
            TimeProvider.System,
            Options.Create(new ModelOptions()),
            NullLogger<MissionOrchestrator>.Instance);

        var mission = await orchestrator.CreateMissionAsync(new CreateMissionRequest
        {
            Title = "Queue test",
            Prompt = "Queue mission",
            SelectedRepository = new RepositoryRef { Owner = "local", Name = "apex", FullName = "local/apex", DefaultBranch = "main" },
            SelectedSprint = new SprintRef { Id = "7", Number = 7, Title = "Sprint 7", State = "open" }
        }, CancellationToken.None);

        Assert.Equal(MissionStatus.Queued, mission.Status);
        Assert.Equal(1, orchestrator.QueueDepth);
        Assert.Equal("local/apex", mission.SelectedRepository?.FullName);
        Assert.Equal("Sprint 7", mission.SelectedSprint?.Title);
        Assert.Contains(await progressStore.GetByMissionAsync(mission.Id, 10, CancellationToken.None), item => item.Stage == "queued");
    }

    [Fact]
    public async Task AdaptiveExecutor_ToolLoopCreatesAlreadyAppliedPatch()
    {
        var workspaceToolset = new LoopWorkspaceToolset();
        var catalogStore = new InMemoryRuntimeCatalogStore();
        var executor = new AdaptiveAgentExecutor(
            AgentRole.Frontend,
            new SequenceModelGateway([
                "{\"kind\":\"tool\",\"toolName\":\"write_file\",\"arguments\":{\"path\":\"frontend/src/App.tsx\",\"content\":\"export default function App() { return <main>agent</main> }\"}}",
                "{\"kind\":\"finish\",\"summary\":\"UI update completed.\"}"
            ]),
            workspaceToolset,
            catalogStore,
            Options.Create(new ModelOptions()),
            Options.Create(new RuntimeOptions()),
            TimeProvider.System);

        var mission = BuildMission();
        var result = await executor.ExecuteAsync(new AgentExecutionContext(mission, BuildWorkspace(), [], null), CancellationToken.None);

        var patch = Assert.Single(result.ProposedPatches);
        Assert.True(patch.AlreadyApplied);
        Assert.Equal(PatchProposalStatus.PendingReview, patch.Status);
        Assert.Contains("frontend/src/App.tsx", patch.TargetPaths);
        Assert.Contains("UI update completed.", result.Summary, StringComparison.Ordinal);
    }

    private static Mission BuildMission()
    {
        return new Mission
        {
            Id = Guid.NewGuid(),
            Title = "Demo mission",
            Prompt = "Build a local-first agent dashboard with patch review.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SelectedRepository = new RepositoryRef { Owner = "local", Name = "apex", FullName = "local/apex", DefaultBranch = "main" },
            SelectedSprint = new SprintRef { Id = "12", Number = 12, Title = "Sprint 12", State = "open" },
            AutoCreatePullRequest = true,
            Agents = Enum.GetValues<AgentRole>().Select(role => new AgentSnapshot { Role = role, Label = role.ToString(), UpdatedAt = DateTimeOffset.UtcNow }).ToList()
        };
    }

    private static WorkspaceSnapshot BuildWorkspace()
    {
        return new WorkspaceSnapshot(
            "repo",
            ["src/Apex.AgentTeam.Api/Program.cs", "frontend/src/App.tsx"],
            new Dictionary<string, string>
            {
                ["src/Apex.AgentTeam.Api/Program.cs"] = "var builder = WebApplication.CreateBuilder(args);",
                ["frontend/src/App.tsx"] = "export default function App() { return null }"
            });
    }

    private sealed class FakeModelGateway : IModelGateway
    {
        private readonly string _response;

        public FakeModelGateway(string response)
        {
            _response = response;
        }

        public Task<ModelTextResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModelTextResponse(_response, false));
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelInfo>>([new OllamaModelInfo { Name = "qwen2.5-coder:14b" }]);
        }

        public Task<ModelChatResponse> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModelChatResponse("assistant", new ChatUsage { EvalCount = 1, PromptEvalCount = 1 }, false));
        }
    }

    private sealed class SequenceModelGateway : IModelGateway
    {
        private readonly Queue<string> _chatResponses;

        public SequenceModelGateway(IEnumerable<string> chatResponses)
        {
            _chatResponses = new Queue<string>(chatResponses);
        }

        public Task<ModelTextResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModelTextResponse("fallback", false));
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelInfo>>([new OllamaModelInfo { Name = "qwen2.5-coder:14b" }]);
        }

        public Task<ModelChatResponse> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        {
            var next = _chatResponses.Count > 0 ? _chatResponses.Dequeue() : "{\"kind\":\"finish\",\"summary\":\"done\"}";
            return Task.FromResult(new ModelChatResponse(next, null, false));
        }
    }

    private sealed class InMemoryMissionRepository : IMissionRepository
    {
        private readonly Dictionary<Guid, Mission> _missions = [];
        private readonly Dictionary<Guid, List<ActivityEvent>> _activities = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveMissionAsync(Mission mission, CancellationToken cancellationToken)
        {
            _missions[mission.Id] = mission;
            return Task.CompletedTask;
        }

        public Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken)
        {
            _missions.TryGetValue(missionId, out var mission);
            return Task.FromResult(mission);
        }

        public Task<Mission?> GetLatestMissionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_missions.Values.OrderByDescending(mission => mission.UpdatedAt).FirstOrDefault());
        }

        public Task<IReadOnlyList<ActivityEvent>> GetActivitiesAsync(Guid missionId, CancellationToken cancellationToken)
        {
            _activities.TryGetValue(missionId, out var items);
            return Task.FromResult<IReadOnlyList<ActivityEvent>>(items ?? []);
        }

        public Task AppendActivityAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
        {
            if (!_activities.TryGetValue(activityEvent.MissionId, out var items))
            {
                items = [];
                _activities[activityEvent.MissionId] = items;
            }

            items.Add(activityEvent);
            return Task.CompletedTask;
        }

        public Task<(Mission Mission, PatchProposal Proposal)?> FindPatchProposalAsync(Guid proposalId, CancellationToken cancellationToken)
        {
            foreach (var mission in _missions.Values)
            {
                var proposal = mission.PatchProposals.FirstOrDefault(item => item.Id == proposalId);
                if (proposal is not null)
                {
                    return Task.FromResult<(Mission Mission, PatchProposal Proposal)?>(new(mission, proposal));
                }
            }

            return Task.FromResult<(Mission Mission, PatchProposal Proposal)?>(null);
        }
    }

    private sealed class InMemoryProgressStore : IProgressLogStore
    {
        private readonly List<ProgressLog> _items = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AppendAsync(ProgressLog progressLog, CancellationToken cancellationToken)
        {
            _items.Add(progressLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProgressLog>> GetByMissionAsync(Guid missionId, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ProgressLog>>(_items.Where(item => item.MissionId == missionId).TakeLast(limit).ToList());
        }
    }

    private sealed class NullActivityStream : IActivityStream
    {
        public Task PublishAsync(ActivityEvent activityEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullProgressStream : IProgressStream
    {
        public Task PublishAsync(ProgressLog progressLog, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullMemoryStore : IMemoryStore
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);
        }
    }

    private sealed class NullWorkspaceToolset : IWorkspaceToolset
    {
        public Task<WorkspaceSnapshot> CaptureSnapshotAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildWorkspace());
        }

        public Task<string> ListFilesAsync(Mission mission, string? pattern, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult("frontend/src/App.tsx");
        }

        public Task<string> ReadFileAsync(Mission mission, string relativePath, int startLine, int maxLines, CancellationToken cancellationToken)
        {
            return Task.FromResult("   1: test");
        }

        public Task<string> WriteFileAsync(Mission mission, string relativePath, string content, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Wrote {relativePath}");
        }

        public Task<string> SearchCodeAsync(Mission mission, string query, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult("frontend/src/App.tsx:1:test");
        }

        public Task<string> RunTerminalCommandAsync(Mission mission, string command, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<string> GetGitStatusAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<string> GetGitDiffAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult("Working tree clean.");
        }

        public Task<string> CommitAsync(Mission mission, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<string> PushAsync(Mission mission, string? branchName, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<PatchApplyResult> ApplyPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PatchApplyResult(true, string.Empty, string.Empty));
        }

        public Task<PatchApplyResult> RevertPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PatchApplyResult(true, string.Empty, string.Empty));
        }

        public Task<TestRunResult> RunValidationAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TestRunResult(true, "ok"));
        }

        public Task<WorkspaceBranchResult> PublishBranchAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new WorkspaceBranchResult(true, "apex/test", "main", "ok", "repo"));
        }
    }

    private sealed class LoopWorkspaceToolset : IWorkspaceToolset
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase)
        {
            ["frontend/src/App.tsx"] = "export default function App() { return null }"
        };

        public Task<WorkspaceSnapshot> CaptureSnapshotAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildWorkspace());
        }

        public Task<string> ListFilesAsync(Mission mission, string? pattern, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Join('\n', _files.Keys));
        }

        public Task<string> ReadFileAsync(Mission mission, string relativePath, int startLine, int maxLines, CancellationToken cancellationToken)
        {
            return Task.FromResult(_files.TryGetValue(relativePath, out var content) ? content : string.Empty);
        }

        public Task<string> WriteFileAsync(Mission mission, string relativePath, string content, CancellationToken cancellationToken)
        {
            _files[relativePath] = content;
            return Task.FromResult($"Wrote {relativePath}");
        }

        public Task<string> SearchCodeAsync(Mission mission, string query, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Join('\n', _files.Where(item => item.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).Select(item => item.Key)));
        }

        public Task<string> RunTerminalCommandAsync(Mission mission, string command, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<string> GetGitStatusAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0\n M frontend/src/App.tsx");
        }

        public Task<string> GetGitDiffAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult("""
                diff --git a/frontend/src/App.tsx b/frontend/src/App.tsx
                --- a/frontend/src/App.tsx
                +++ b/frontend/src/App.tsx
                @@ -1 +1 @@
                -export default function App() { return null }
                +export default function App() { return <main>agent</main> }
                """);
        }

        public Task<string> CommitAsync(Mission mission, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult($"ExitCode: 0\n[{message}]");
        }

        public Task<string> PushAsync(Mission mission, string? branchName, CancellationToken cancellationToken)
        {
            return Task.FromResult("ExitCode: 0");
        }

        public Task<PatchApplyResult> ApplyPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PatchApplyResult(true, string.Empty, string.Empty));
        }

        public Task<PatchApplyResult> RevertPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PatchApplyResult(true, string.Empty, string.Empty));
        }

        public Task<TestRunResult> RunValidationAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TestRunResult(true, "ok"));
        }

        public Task<WorkspaceBranchResult> PublishBranchAsync(Mission mission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new WorkspaceBranchResult(true, "apex/test", "main", "ok", "repo"));
        }
    }

    private sealed class NullTaskSink : IExternalTaskSink
    {
        public Task<ExternalTaskRef> CreateTaskAsync(ExternalTaskDraft draft, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExternalTaskRef { Provider = "github", ExternalId = "1", Title = draft.Title, Status = "Created" });
        }
    }

    private sealed class NullGitHubCatalog : IGitHubCatalog
    {
        public Task<IReadOnlyList<RepositoryRef>> ListRepositoriesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RepositoryRef>>([]);
        }

        public Task<IReadOnlyList<SprintRef>> ListMilestonesAsync(string owner, string repository, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SprintRef>>([]);
        }

        public Task<IReadOnlyList<SprintRef>> EnsureDefaultMilestonesAsync(string owner, string repository, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SprintRef>>([]);
        }

        public Task<GitHubBoardSnapshot> GetRepositoryBoardAsync(string owner, string repository, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitHubBoardSnapshot());
        }

        public Task<PullRequestRef?> CreatePullRequestAsync(Mission mission, WorkspaceBranchResult branchResult, CancellationToken cancellationToken)
        {
            return Task.FromResult<PullRequestRef?>(new PullRequestRef
            {
                Provider = "github",
                ExternalId = "1",
                Title = mission.Title,
                Status = "Created",
                HeadBranch = branchResult.BranchName,
                BaseBranch = branchResult.BaseBranch,
                Url = "https://example.test/pr/1"
            });
        }
    }

    private sealed class InMemoryRuntimeCatalogStore : IAgentRuntimeCatalogStore
    {
        private readonly AgentRuntimeCatalog _catalog = new()
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Tools =
            [
                new AgentToolDefinition { Name = "write_file", DisplayName = "Write File", Description = "Write", Type = AgentToolType.WriteFile, Enabled = true, Destructive = true },
                new AgentToolDefinition { Name = "git_diff", DisplayName = "Git Diff", Description = "Diff", Type = AgentToolType.GitDiff, Enabled = true }
            ],
            Policies =
            [
                new AgentRolePolicy
                {
                    Role = AgentRole.Frontend,
                    ExecutionMode = AgentExecutionMode.ToolLoop,
                    AllowedTools = ["write_file", "git_diff"],
                    WritableRoots = ["frontend", "src"],
                    MaxSteps = 4
                }
            ]
        };

        public Task<AgentRuntimeCatalog> GetCatalogAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_catalog);
        }

        public Task<AgentToolDefinition> UpsertToolAsync(UpsertAgentToolRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<AgentRolePolicy> UpdatePolicyAsync(AgentRole role, UpdateAgentRolePolicyRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
