using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.Extensions.Options;

namespace Apex.AgentTeam.Api.Services;

public sealed class JsonAgentRuntimeCatalogStore : IAgentRuntimeCatalogStore
{
    private readonly RuntimeOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAgentRuntimeCatalogStore(IOptions<RuntimeOptions> options, IHostEnvironment environment, TimeProvider timeProvider)
    {
        _options = options.Value;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public async Task<AgentRuntimeCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogCoreAsync(cancellationToken);
            return CloneCatalog(catalog);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentToolDefinition> UpsertToolAsync(UpsertAgentToolRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogCoreAsync(cancellationToken);
            var normalizedName = NormalizeToolName(request.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new InvalidOperationException("Tool name is required.");
            }

            if (request.Type == AgentToolType.CustomCommand && string.IsNullOrWhiteSpace(request.CommandTemplate))
            {
                throw new InvalidOperationException("Custom command tools require a command template.");
            }

            var existing = catalog.Tools.FirstOrDefault(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new AgentToolDefinition { Name = normalizedName };
                catalog.Tools.Add(existing);
            }

            existing.Name = normalizedName;
            existing.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? normalizedName : request.DisplayName.Trim();
            existing.Description = request.Description?.Trim() ?? string.Empty;
            existing.Type = request.Type;
            existing.Enabled = request.Enabled;
            existing.Destructive = request.Destructive;
            existing.CommandTemplate = string.IsNullOrWhiteSpace(request.CommandTemplate) ? null : request.CommandTemplate.Trim();

            NormalizeCatalog(catalog);
            await SaveCatalogCoreAsync(catalog, cancellationToken);
            return CloneTool(existing);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentRolePolicy> UpdatePolicyAsync(AgentRole role, UpdateAgentRolePolicyRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogCoreAsync(cancellationToken);
            var policy = catalog.Policies.FirstOrDefault(item => item.Role == role);
            if (policy is null)
            {
                policy = new AgentRolePolicy { Role = role };
                catalog.Policies.Add(policy);
            }

            policy.ExecutionMode = request.ExecutionMode;
            policy.MaxSteps = Math.Clamp(request.MaxSteps, 1, 24);
            policy.AllowedTools = request.AllowedTools
                .Select(NormalizeToolName)
                .Where(item => catalog.Tools.Any(tool => string.Equals(tool.Name, item, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
            policy.AllowedDelegates = request.AllowedDelegates
                .Where(candidate => candidate != role)
                .Distinct()
                .OrderBy(candidate => candidate.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            policy.WritableRoots = request.WritableRoots
                .Select(NormalizePathPrefix)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            NormalizeCatalog(catalog);
            await SaveCatalogCoreAsync(catalog, cancellationToken);
            return ClonePolicy(policy);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AgentRuntimeCatalog> LoadCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var path = GetCatalogPath();
        if (!File.Exists(path))
        {
            var defaultCatalog = CreateDefaultCatalog();
            await SaveCatalogCoreAsync(defaultCatalog, cancellationToken);
            return defaultCatalog;
        }

        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<AgentRuntimeCatalog>(stream, JsonDefaults.Web, cancellationToken);
        if (catalog is null)
        {
            catalog = CreateDefaultCatalog();
        }

        NormalizeCatalog(catalog);
        return catalog;
    }

    private async Task SaveCatalogCoreAsync(AgentRuntimeCatalog catalog, CancellationToken cancellationToken)
    {
        catalog.UpdatedAt = _timeProvider.GetUtcNow();
        NormalizeCatalog(catalog);

        var path = GetCatalogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, catalog, JsonDefaults.Web, cancellationToken);
    }

    private string GetCatalogPath()
    {
        return Path.GetFullPath(_options.RegistryPath, _environment.ContentRootPath);
    }

    private AgentRuntimeCatalog CreateDefaultCatalog()
    {
        return new AgentRuntimeCatalog
        {
            UpdatedAt = _timeProvider.GetUtcNow(),
            Tools =
            [
                new AgentToolDefinition { Name = "list_files", DisplayName = "List Files", Description = "List files under the workspace root.", Type = AgentToolType.ListFiles, Enabled = true },
                new AgentToolDefinition { Name = "read_file", DisplayName = "Read File", Description = "Read a slice of a file with line numbers.", Type = AgentToolType.ReadFile, Enabled = true },
                new AgentToolDefinition { Name = "write_file", DisplayName = "Write File", Description = "Overwrite or create a file inside the workspace.", Type = AgentToolType.WriteFile, Enabled = true, Destructive = true },
                new AgentToolDefinition { Name = "search_code", DisplayName = "Search Code", Description = "Search matching text across source files.", Type = AgentToolType.SearchCode, Enabled = true },
                new AgentToolDefinition { Name = "run_terminal", DisplayName = "Run Terminal", Description = "Run a workspace terminal command.", Type = AgentToolType.RunTerminal, Enabled = true, Destructive = true },
                new AgentToolDefinition { Name = "git_status", DisplayName = "Git Status", Description = "Read the current git status for the workspace.", Type = AgentToolType.GitStatus, Enabled = true },
                new AgentToolDefinition { Name = "git_diff", DisplayName = "Git Diff", Description = "Read the current git diff for changed files.", Type = AgentToolType.GitDiff, Enabled = true },
                new AgentToolDefinition { Name = "git_commit", DisplayName = "Git Commit", Description = "Commit all current workspace changes.", Type = AgentToolType.GitCommit, Enabled = true, Destructive = true },
                new AgentToolDefinition { Name = "git_push", DisplayName = "Git Push", Description = "Push the active branch to origin.", Type = AgentToolType.GitPush, Enabled = true, Destructive = true }
            ],
            Policies =
            [
                new AgentRolePolicy { Role = AgentRole.Manager, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = [], AllowedDelegates = [AgentRole.Analyst, AgentRole.WebDev, AgentRole.Frontend, AgentRole.Backend, AgentRole.Tester, AgentRole.PM, AgentRole.Support], WritableRoots = [], MaxSteps = 2 },
                new AgentRolePolicy { Role = AgentRole.Analyst, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = ["list_files", "read_file", "search_code", "git_status"], AllowedDelegates = [AgentRole.WebDev, AgentRole.Frontend, AgentRole.Backend, AgentRole.Support], WritableRoots = [], MaxSteps = 4 },
                new AgentRolePolicy { Role = AgentRole.WebDev, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = ["list_files", "read_file", "search_code", "git_status", "git_diff"], AllowedDelegates = [AgentRole.Frontend, AgentRole.Backend, AgentRole.Tester], WritableRoots = [], MaxSteps = 4 },
                new AgentRolePolicy { Role = AgentRole.Frontend, ExecutionMode = AgentExecutionMode.ToolLoop, AllowedTools = ["list_files", "read_file", "write_file", "search_code", "run_terminal", "git_status", "git_diff"], AllowedDelegates = [AgentRole.Tester, AgentRole.PM], WritableRoots = ["frontend", "src"], MaxSteps = 8 },
                new AgentRolePolicy { Role = AgentRole.Backend, ExecutionMode = AgentExecutionMode.ToolLoop, AllowedTools = ["list_files", "read_file", "write_file", "search_code", "run_terminal", "git_status", "git_diff"], AllowedDelegates = [AgentRole.Tester, AgentRole.PM], WritableRoots = ["src", "tests"], MaxSteps = 8 },
                new AgentRolePolicy { Role = AgentRole.Tester, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = ["list_files", "read_file", "search_code", "run_terminal", "git_status", "git_diff"], AllowedDelegates = [AgentRole.PM], WritableRoots = [], MaxSteps = 5 },
                new AgentRolePolicy { Role = AgentRole.PM, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = ["read_file", "git_status", "git_diff"], AllowedDelegates = [AgentRole.Support], WritableRoots = [], MaxSteps = 3 },
                new AgentRolePolicy { Role = AgentRole.Support, ExecutionMode = AgentExecutionMode.StructuredPrompt, AllowedTools = ["read_file"], AllowedDelegates = [], WritableRoots = [], MaxSteps = 3 }
            ]
        };
    }

    private static void NormalizeCatalog(AgentRuntimeCatalog catalog)
    {
        foreach (var tool in catalog.Tools)
        {
            tool.Name = NormalizeToolName(tool.Name);
            tool.DisplayName = string.IsNullOrWhiteSpace(tool.DisplayName) ? tool.Name : tool.DisplayName.Trim();
            tool.Description = tool.Description?.Trim() ?? string.Empty;
            tool.CommandTemplate = string.IsNullOrWhiteSpace(tool.CommandTemplate) ? null : tool.CommandTemplate.Trim();
        }

        catalog.Tools = catalog.Tools
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => CloneTool(group.Last()))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.Policies = catalog.Policies
            .GroupBy(item => item.Role)
            .Select(group => ClonePolicy(group.Last()))
            .OrderBy(item => item.Role.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var policy in catalog.Policies)
        {
            policy.MaxSteps = Math.Clamp(policy.MaxSteps, 1, 24);
            policy.AllowedTools = policy.AllowedTools
                .Select(NormalizeToolName)
                .Where(item => catalog.Tools.Any(tool => string.Equals(tool.Name, item, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
            policy.AllowedDelegates = policy.AllowedDelegates
                .Where(candidate => candidate != policy.Role)
                .Distinct()
                .OrderBy(candidate => candidate.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            policy.WritableRoots = policy.WritableRoots
                .Select(NormalizePathPrefix)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static AgentRuntimeCatalog CloneCatalog(AgentRuntimeCatalog source)
    {
        return new AgentRuntimeCatalog
        {
            UpdatedAt = source.UpdatedAt,
            Tools = source.Tools.Select(CloneTool).ToList(),
            Policies = source.Policies.Select(ClonePolicy).ToList()
        };
    }

    private static AgentToolDefinition CloneTool(AgentToolDefinition source)
    {
        return new AgentToolDefinition
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Type = source.Type,
            Enabled = source.Enabled,
            Destructive = source.Destructive,
            CommandTemplate = source.CommandTemplate
        };
    }

    private static AgentRolePolicy ClonePolicy(AgentRolePolicy source)
    {
        return new AgentRolePolicy
        {
            Role = source.Role,
            ExecutionMode = source.ExecutionMode,
            AllowedTools = source.AllowedTools.ToList(),
            AllowedDelegates = source.AllowedDelegates.ToList(),
            WritableRoots = source.WritableRoots.ToList(),
            MaxSteps = source.MaxSteps
        };
    }

    private static string NormalizeToolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static string NormalizePathPrefix(string value)
    {
        return (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
    }
}

public sealed class AdaptiveAgentExecutor : IAgentExecutor
{
    private readonly StructuredAgentExecutor _structuredExecutor;
    private readonly ToolLoopAgentExecutor _toolLoopExecutor;
    private readonly IAgentRuntimeCatalogStore _catalogStore;

    public AdaptiveAgentExecutor(
        AgentRole role,
        IModelGateway modelGateway,
        IWorkspaceToolset workspaceToolset,
        IAgentRuntimeCatalogStore catalogStore,
        IOptions<ModelOptions> modelOptions,
        IOptions<RuntimeOptions> runtimeOptions,
        TimeProvider timeProvider)
    {
        Role = role;
        _structuredExecutor = new StructuredAgentExecutor(role, modelGateway, timeProvider);
        _toolLoopExecutor = new ToolLoopAgentExecutor(role, modelGateway, workspaceToolset, modelOptions, runtimeOptions, timeProvider);
        _catalogStore = catalogStore;
    }

    public AgentRole Role { get; }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken)
    {
        var catalog = await _catalogStore.GetCatalogAsync(cancellationToken);
        var policy = catalog.Policies.FirstOrDefault(item => item.Role == Role);
        if (policy?.ExecutionMode != AgentExecutionMode.ToolLoop)
        {
            return await _structuredExecutor.ExecuteAsync(context, cancellationToken);
        }

        try
        {
            return await _toolLoopExecutor.ExecuteAsync(context, policy, catalog.Tools, cancellationToken);
        }
        catch (Exception exception)
        {
            if (context.ProgressCallback is not null)
            {
                await context.ProgressCallback(
                    new AgentExecutionUpdate("tool-loop-fallback", $"Tool loop failed for {Role}: {exception.Message}"),
                    cancellationToken);
            }

            return await _structuredExecutor.ExecuteAsync(context, cancellationToken);
        }
    }
}

internal sealed class ToolLoopAgentExecutor
{
    private readonly IModelGateway _modelGateway;
    private readonly IWorkspaceToolset _workspaceToolset;
    private readonly ModelOptions _modelOptions;
    private readonly RuntimeOptions _runtimeOptions;
    private readonly TimeProvider _timeProvider;

    public ToolLoopAgentExecutor(
        AgentRole role,
        IModelGateway modelGateway,
        IWorkspaceToolset workspaceToolset,
        IOptions<ModelOptions> modelOptions,
        IOptions<RuntimeOptions> runtimeOptions,
        TimeProvider timeProvider)
    {
        Role = role;
        _modelGateway = modelGateway;
        _workspaceToolset = workspaceToolset;
        _modelOptions = modelOptions.Value;
        _runtimeOptions = runtimeOptions.Value;
        _timeProvider = timeProvider;
    }

    public AgentRole Role { get; }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionContext context,
        AgentRolePolicy policy,
        IReadOnlyList<AgentToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var toolIndex = tools.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<ChatMessage>
        {
            BuildMessage("system", BuildSystemPrompt(policy, tools)),
            BuildMessage("user", BuildUserPrompt(context, policy))
        };

        var transcript = new StringBuilder();
        var summary = string.Empty;
        var invalidResponses = 0;

        for (var step = 1; step <= policy.MaxSteps; step++)
        {
            if (context.ProgressCallback is not null)
            {
                await context.ProgressCallback(
                    new AgentExecutionUpdate("tool-loop", $"{Role} deciding step {step}/{policy.MaxSteps}."),
                    cancellationToken);
            }

            var response = await _modelGateway.ChatAsync(_modelOptions.ChatModel, messages, cancellationToken);
            transcript.AppendLine($"STEP {step} MODEL").AppendLine(response.Content).AppendLine();
            var action = ParseAction(response.Content);
            if (action is null)
            {
                invalidResponses++;
                if (invalidResponses >= 2)
                {
                    summary = $"Tool loop returned invalid JSON twice. Last response: {Truncate(response.Content, 320)}";
                    break;
                }

                messages.Add(BuildMessage("assistant", response.Content));
                messages.Add(BuildMessage("user", "Return strict JSON only. Use either {\"kind\":\"tool\",...} or {\"kind\":\"finish\",...}."));
                continue;
            }

            messages.Add(BuildMessage("assistant", response.Content));
            if (string.Equals(action.Kind, "finish", StringComparison.OrdinalIgnoreCase))
            {
                summary = string.IsNullOrWhiteSpace(action.Summary)
                    ? $"Tool loop finished after {step} steps."
                    : action.Summary.Trim();
                break;
            }

            var observation = await ExecuteToolAsync(context, policy, toolIndex, action, cancellationToken);
            transcript.AppendLine($"STEP {step} TOOL {action.ToolName}")
                .AppendLine(observation)
                .AppendLine();

            messages.Add(BuildMessage(
                "user",
                $"Tool result for '{action.ToolName}':\n{observation}\nIf the task is complete, return finish JSON. Otherwise choose the next tool."));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"Tool loop stopped after {policy.MaxSteps} steps.";
        }

        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Role.ToString()] = summary,
            [$"{Role}-tool-loop"] = transcript.ToString()
        };

        var proposedPatches = new List<PatchProposal>();
        if (Role is AgentRole.Frontend or AgentRole.Backend)
        {
            var diff = await _workspaceToolset.GetGitDiffAsync(context.Mission, cancellationToken);
            var changedPaths = ExtractChangedPaths(diff);
            if (changedPaths.Count > 0 && !string.IsNullOrWhiteSpace(diff) && !string.Equals(diff.Trim(), "Working tree clean.", StringComparison.Ordinal))
            {
                proposedPatches.Add(new PatchProposal
                {
                    Id = Guid.NewGuid(),
                    MissionId = context.Mission.Id,
                    AuthorRole = Role,
                    Title = $"{Role} workspace patch for {context.Mission.Title}",
                    Summary = Truncate(summary, 240),
                    Status = PatchProposalStatus.PendingReview,
                    TargetPaths = changedPaths,
                    Diff = diff,
                    AlreadyApplied = true,
                    CreatedAt = _timeProvider.GetUtcNow()
                });
            }
        }

        return new AgentExecutionResult(summary, [], proposedPatches, artifacts, null);
    }

    private async Task<string> ExecuteToolAsync(
        AgentExecutionContext context,
        AgentRolePolicy policy,
        IReadOnlyDictionary<string, AgentToolDefinition> toolIndex,
        ToolLoopAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.ToolName))
        {
            return "Blocked: tool name is required.";
        }

        if (!toolIndex.TryGetValue(action.ToolName, out var tool))
        {
            return $"Blocked: tool '{action.ToolName}' is not registered.";
        }

        if (!tool.Enabled)
        {
            return $"Blocked: tool '{tool.Name}' is disabled.";
        }

        if (!policy.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            return $"Blocked: role {Role} is not allowed to use '{tool.Name}'.";
        }

        try
        {
            var result = tool.Type switch
            {
                AgentToolType.ListFiles => await _workspaceToolset.ListFilesAsync(context.Mission, GetPattern(action.Arguments), GetInt(action.Arguments, "limit", 80), cancellationToken),
                AgentToolType.ReadFile => await _workspaceToolset.ReadFileAsync(context.Mission, GetRequiredPath(action.Arguments), GetStartLine(action.Arguments), GetMaxLines(action.Arguments), cancellationToken),
                AgentToolType.WriteFile => await ExecuteWriteFileAsync(context, policy, action, cancellationToken),
                AgentToolType.SearchCode => await _workspaceToolset.SearchCodeAsync(context.Mission, GetRequiredValue(action.Arguments, "query", "SearchCode requires a non-empty 'query' argument."), GetInt(action.Arguments, "limit", 20), cancellationToken),
                AgentToolType.RunTerminal => await _workspaceToolset.RunTerminalCommandAsync(context.Mission, GetRequiredValue(action.Arguments, "command", "RunTerminal requires a non-empty 'command' argument."), cancellationToken),
                AgentToolType.GitStatus => await _workspaceToolset.GetGitStatusAsync(context.Mission, cancellationToken),
                AgentToolType.GitDiff => await _workspaceToolset.GetGitDiffAsync(context.Mission, cancellationToken),
                AgentToolType.GitCommit => await _workspaceToolset.CommitAsync(context.Mission, GetValue(action.Arguments, "message") ?? $"Apex AI: {Role}", cancellationToken),
                AgentToolType.GitPush => await _workspaceToolset.PushAsync(context.Mission, GetValue(action.Arguments, "branchName"), cancellationToken),
                AgentToolType.CustomCommand => await _workspaceToolset.RunTerminalCommandAsync(context.Mission, RenderCustomCommand(tool, action.Arguments), cancellationToken),
                _ => $"Blocked: unsupported tool type '{tool.Type}'."
            };

            var normalized = Truncate(result, _runtimeOptions.MaxToolOutputCharacters);
            if (context.ProgressCallback is not null)
            {
                await context.ProgressCallback(
                    new AgentExecutionUpdate($"tool-{tool.Name}", $"{Role} used {tool.Name}.", new Dictionary<string, string>
                    {
                        ["tool"] = tool.Name,
                        ["output"] = Truncate(normalized, 400)
                    }),
                    cancellationToken);
            }

            return normalized;
        }
        catch (Exception exception)
        {
            var message = $"Tool '{tool.Name}' failed: {exception.Message}";
            if (context.ProgressCallback is not null)
            {
                await context.ProgressCallback(
                    new AgentExecutionUpdate($"tool-{tool.Name}-failed", message, new Dictionary<string, string>
                    {
                        ["tool"] = tool.Name
                    }),
                    cancellationToken);
            }

            return message;
        }
    }

    private async Task<string> ExecuteWriteFileAsync(
        AgentExecutionContext context,
        AgentRolePolicy policy,
        ToolLoopAction action,
        CancellationToken cancellationToken)
    {
        var path = GetRequiredPath(action.Arguments);
        if (!IsWritablePath(policy, path))
        {
            return $"Blocked: '{path}' is outside writable roots for {Role}. Allowed roots: {string.Join(", ", policy.WritableRoots)}";
        }

        var content = GetRequiredValue(action.Arguments, "content", "WriteFile requires a non-empty 'content' argument.");
        return await _workspaceToolset.WriteFileAsync(context.Mission, path, content, cancellationToken);
    }

    private string BuildSystemPrompt(AgentRolePolicy policy, IReadOnlyList<AgentToolDefinition> tools)
    {
        var toolLines = tools
            .Where(item => item.Enabled && policy.AllowedTools.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .Select(item =>
            {
                var contract = GetToolContract(item.Type);
                return $"- {item.Name}: {item.Description} (type: {item.Type}){Environment.NewLine}  args: {contract.ArgumentSummary}{Environment.NewLine}  example: {contract.ExampleJson}";
            })
            .ToList();

        var writableRoots = policy.WritableRoots.Count == 0 ? "Read-only by policy." : string.Join(", ", policy.WritableRoots);

        return $@"You are the {Role} agent inside an autonomous software team.
Your job is to do the work by choosing tools, observing results, and continuing until you can finish.
Only use registered tools. Do not invent tool names.
Reply with JSON only. Never add markdown fences.

JSON schema:
{{
  ""kind"": ""tool"" | ""finish"",
  ""toolName"": ""registered_tool_name"",
  ""arguments"": {{ ""argName"": ""value"" }},
  ""summary"": ""only for finish""
}}

Rules:
- Use as many tool steps as needed, up to {policy.MaxSteps}.
- Prefer reading files and checking git status before writing.
- If you write files, keep the changes aligned to your role.
- Writable roots: {writableRoots}
- When the task is complete, return {{""kind"":""finish"",""summary"":""...""}}.
- If a tool is blocked, adapt and choose another tool.
- Use the exact argument names shown in the contracts below.
- Accepted aliases are normalized server-side, but the preferred names are still the contract names.

Allowed tools:
{string.Join(Environment.NewLine, toolLines)}";
    }

    private string BuildUserPrompt(AgentExecutionContext context, AgentRolePolicy policy)
    {
        var knowledge = context.Knowledge.Count == 0
            ? "No knowledge hits found."
            : string.Join(Environment.NewLine, context.Knowledge.Take(4).Select(item => $"- {item.SourcePath}: {Truncate(item.Content, 240)}"));

        var previews = context.Workspace.FilePreviews.Take(6)
            .Select(item => $"FILE {item.Key}{Environment.NewLine}{Truncate(item.Value, 420)}")
            .ToList();

        return $"""
            Mission title: {context.Mission.Title}
            Mission objective:
            {GetMissionObjective(context.Mission)}

            Repository:
            {context.Mission.SelectedRepository?.FullName ?? "Not selected"}

            Previous summary:
            {context.PreviousSummary ?? "None"}

            Workspace root:
            {context.Workspace.RootPath}

            Sample files:
            {string.Join(", ", context.Workspace.Files.Take(24))}

            Writable roots for this role:
            {(policy.WritableRoots.Count == 0 ? "Read-only" : string.Join(", ", policy.WritableRoots))}

            Knowledge hits:
            {knowledge}

            File previews:
            {string.Join($"{Environment.NewLine}{Environment.NewLine}", previews)}
            """;
    }

    private static ChatMessage BuildMessage(string role, string content)
    {
        return new ChatMessage
        {
            Id = Guid.NewGuid(),
            ThreadId = Guid.Empty,
            Role = role,
            Content = content,
            Model = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ToolLoopAction? ParseAction(string raw)
    {
        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var kind = GetJsonString(root, "kind");
            var toolName = GetJsonString(root, "toolName")
                ?? GetJsonString(root, "tool")
                ?? (string.Equals(kind, "tool", StringComparison.OrdinalIgnoreCase) ? GetJsonString(root, "action") : null);
            var summary = GetJsonString(root, "summary") ?? GetJsonString(root, "finalMessage");
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentsElement.EnumerateObject())
                {
                    arguments[property.Name] = JsonElementToString(property.Value);
                }
            }
            else
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("kind") || property.NameEquals("toolName") || property.NameEquals("tool") || property.NameEquals("action") || property.NameEquals("summary") || property.NameEquals("finalMessage"))
                    {
                        continue;
                    }

                    arguments[property.Name] = JsonElementToString(property.Value);
                }
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                kind = string.IsNullOrWhiteSpace(toolName) ? "finish" : "tool";
            }

            if (string.Equals(kind, "finish", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolLoopAction("finish", null, arguments, summary);
            }

            if (string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var normalizedToolName = NormalizeToolName(toolName);
            return new ToolLoopAction("tool", normalizedToolName, NormalizeArguments(normalizedToolName, arguments), summary);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        trimmed = Regex.Replace(trimmed, "^```(?:json)?\\s*", string.Empty, RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, "\\s*```$", string.Empty, RegexOptions.IgnoreCase);

        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first)
        {
            return trimmed[first..(last + 1)];
        }

        return trimmed;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? JsonElementToString(property) : null;
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.ToString()
        };
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value : null;
    }

    private static string GetRequiredValue(IReadOnlyDictionary<string, string> arguments, string key, string errorMessage)
    {
        var value = GetValue(arguments, key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int fallback)
    {
        return arguments.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string GetRequiredPath(IReadOnlyDictionary<string, string> arguments)
    {
        var path = GetValue(arguments, "path")?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A file path is required. Use 'path' with a workspace-relative file path such as 'frontend/src/App.tsx'.");
        }

        return path;
    }

    private static string? GetPattern(IReadOnlyDictionary<string, string> arguments)
    {
        return GetValue(arguments, "pattern")?.Trim();
    }

    private static int GetStartLine(IReadOnlyDictionary<string, string> arguments)
    {
        return Math.Max(1, GetInt(arguments, "startLine", 1));
    }

    private static int GetMaxLines(IReadOnlyDictionary<string, string> arguments)
    {
        var startLine = GetStartLine(arguments);
        var maxLines = GetInt(arguments, "maxLines", 220);
        if (arguments.TryGetValue("lineEnd", out var lineEndRaw) && int.TryParse(lineEndRaw, out var lineEnd))
        {
            maxLines = Math.Max(1, lineEnd - startLine + 1);
        }

        return Math.Max(1, maxLines);
    }

    private static bool IsWritablePath(AgentRolePolicy policy, string path)
    {
        if (policy.WritableRoots.Count == 0)
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        return policy.WritableRoots.Any(root =>
            string.Equals(root, ".", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ExtractChangedPaths(string diff)
    {
        return diff.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("diff --git ", StringComparison.Ordinal))
            .Select(line =>
            {
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 4)
                {
                    return string.Empty;
                }

                return tokens[2].StartsWith("a/", StringComparison.Ordinal) ? tokens[2][2..] : tokens[2];
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string RenderCustomCommand(AgentToolDefinition tool, IReadOnlyDictionary<string, string> arguments)
    {
        if (string.IsNullOrWhiteSpace(tool.CommandTemplate))
        {
            return string.Empty;
        }

        var command = tool.CommandTemplate;
        foreach (var pair in arguments)
        {
            command = command.Replace($"{{{{{pair.Key}}}}}", QuoteForShell(pair.Value), StringComparison.OrdinalIgnoreCase);
        }

        return Regex.Replace(command, "{{[^}]+}}", string.Empty);
    }

    private static string QuoteForShell(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
        }

        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string NormalizeToolName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static string Truncate(string value, int limit)
    {
        return value.Length > limit ? value[..limit] : value;
    }

    private static Dictionary<string, string> NormalizeArguments(string toolName, IReadOnlyDictionary<string, string> rawArguments)
    {
        var normalized = new Dictionary<string, string>(rawArguments, StringComparer.OrdinalIgnoreCase);
        CopyAlias(normalized, "file", "path");
        CopyAlias(normalized, "relativePath", "path");
        CopyAlias(normalized, "lineStart", "startLine");
        CopyAlias(normalized, "directory", "pattern");

        if (string.Equals(toolName, "search_code", StringComparison.OrdinalIgnoreCase) && normalized.TryGetValue("directory", out var searchDirectory))
        {
            normalized["pattern"] = searchDirectory;
        }

        return normalized;
    }

    private static void CopyAlias(IDictionary<string, string> arguments, string alias, string canonical)
    {
        if (!arguments.ContainsKey(canonical) && arguments.TryGetValue(alias, out var aliasValue) && !string.IsNullOrWhiteSpace(aliasValue))
        {
            arguments[canonical] = aliasValue;
        }
    }

    private static string GetMissionObjective(Mission mission)
    {
        return string.IsNullOrWhiteSpace(mission.Objective) ? mission.Prompt : mission.Objective;
    }

    private static ToolContract GetToolContract(AgentToolType type)
    {
        return type switch
        {
            AgentToolType.ListFiles => new ToolContract("pattern?: optional folder or glob hint, limit?: optional integer", "{\"kind\":\"tool\",\"toolName\":\"list_files\",\"arguments\":{\"pattern\":\"frontend/src\",\"limit\":60}}"),
            AgentToolType.ReadFile => new ToolContract("path: required, startLine?: integer, maxLines?: integer", "{\"kind\":\"tool\",\"toolName\":\"read_file\",\"arguments\":{\"path\":\"frontend/src/App.tsx\",\"startLine\":1,\"maxLines\":160}}"),
            AgentToolType.WriteFile => new ToolContract("path: required, content: required", "{\"kind\":\"tool\",\"toolName\":\"write_file\",\"arguments\":{\"path\":\"frontend/src/App.tsx\",\"content\":\"export default function App() { return <main /> }\"}}"),
            AgentToolType.SearchCode => new ToolContract("query: required, limit?: optional integer", "{\"kind\":\"tool\",\"toolName\":\"search_code\",\"arguments\":{\"query\":\"createRun\",\"limit\":20}}"),
            AgentToolType.RunTerminal => new ToolContract("command: required", "{\"kind\":\"tool\",\"toolName\":\"run_terminal\",\"arguments\":{\"command\":\"npm run build\"}}"),
            AgentToolType.GitCommit => new ToolContract("message?: optional commit message", "{\"kind\":\"tool\",\"toolName\":\"git_commit\",\"arguments\":{\"message\":\"Apex AI: update run shell\"}}"),
            AgentToolType.GitPush => new ToolContract("branchName?: optional branch name", "{\"kind\":\"tool\",\"toolName\":\"git_push\",\"arguments\":{\"branchName\":\"apex/ui-refresh\"}}"),
            _ => new ToolContract("No arguments required.", "{\"kind\":\"tool\",\"toolName\":\"git_status\",\"arguments\":{}}")
        };
    }

    private sealed record ToolLoopAction(string Kind, string? ToolName, IReadOnlyDictionary<string, string> Arguments, string? Summary);

    private sealed record ToolContract(string ArgumentSummary, string ExampleJson);
}
