using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.Extensions.Options;

namespace Apex.AgentTeam.Api.Services;

public sealed class OllamaModelGateway : IModelGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ModelOptions _options;
    private readonly ILogger<OllamaModelGateway> _logger;

    public OllamaModelGateway(IHttpClientFactory httpClientFactory, IOptions<ModelOptions> options, ILogger<OllamaModelGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ModelTextResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/generate", new
            {
                model = _options.ChatModel,
                system = prompt.SystemPrompt,
                prompt = Truncate(prompt.UserPrompt, 5_000),
                stream = false,
                options = new
                {
                    temperature = prompt.Temperature
                }
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var text = document.RootElement.TryGetProperty("response", out var responseElement)
                ? responseElement.GetString()
                : null;

            return new ModelTextResponse(text?.Trim() ?? BuildFallback(prompt), false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Falling back for model completion for role {Role}.", prompt.Role);
            return new ModelTextResponse(BuildFallback(prompt), true);
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var primaryResponse = await client.PostAsJsonAsync("/api/embed", new
            {
                model = _options.EmbeddingModel,
                input = text
            }, cancellationToken);

            if (primaryResponse.IsSuccessStatusCode)
            {
                var vector = await ReadEmbeddingResponseAsync(primaryResponse, cancellationToken);
                if (vector.Length > 0)
                {
                    return vector;
                }
            }
            else if (primaryResponse.StatusCode != HttpStatusCode.NotFound)
            {
                primaryResponse.EnsureSuccessStatusCode();
            }

            var fallbackResponse = await client.PostAsJsonAsync("/api/embeddings", new
            {
                model = _options.EmbeddingModel,
                prompt = text
            }, cancellationToken);
            fallbackResponse.EnsureSuccessStatusCode();
            var fallbackVector = await ReadEmbeddingResponseAsync(fallbackResponse, cancellationToken);
            if (fallbackVector.Length > 0)
            {
                return fallbackVector;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Embedding fallback activated.");
        }

        return BuildFallbackEmbedding(text);
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<OllamaModelInfo>();
            foreach (var item in modelsElement.EnumerateArray())
            {
                var details = item.TryGetProperty("details", out var detailsElement) ? detailsElement : default;
                DateTimeOffset? modifiedAt = item.TryGetProperty("modified_at", out var modifiedElement) && modifiedElement.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(modifiedElement.GetString(), out var parsedModifiedAt) ? parsedModifiedAt : null;

                models.Add(new OllamaModelInfo
                {
                    Name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
                    Family = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("family", out var familyElement) ? familyElement.GetString() ?? string.Empty : string.Empty,
                    ParameterSize = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("parameter_size", out var parameterElement) ? parameterElement.GetString() ?? string.Empty : string.Empty,
                    ModifiedAt = modifiedAt,
                    Size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size) ? size : 0
                });
            }

            return models;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to list local Ollama models.");
            return [];
        }
    }

    public async Task<ModelChatResponse> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var payload = new
            {
                model,
                stream = false,
                options = new
                {
                    temperature = _options.Temperature
                },
                messages = messages.TakeLast(24).Select(message => new
                {
                    role = NormalizeChatRole(message.Role),
                    content = Truncate(message.Content, 8_000)
                }).ToArray()
            };

            var response = await client.PostAsJsonAsync("/api/chat", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = document.RootElement.TryGetProperty("message", out var messageElement) && messageElement.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;

            var usage = new ChatUsage
            {
                PromptEvalCount = document.RootElement.TryGetProperty("prompt_eval_count", out var promptEvalElement) && promptEvalElement.TryGetInt32(out var promptEvalCount) ? promptEvalCount : null,
                EvalCount = document.RootElement.TryGetProperty("eval_count", out var evalElement) && evalElement.TryGetInt32(out var evalCount) ? evalCount : null
            };

            return new ModelChatResponse(string.IsNullOrWhiteSpace(content) ? "Model returned an empty response." : content.Trim(), usage, false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Chat fallback activated for model {Model}.", model);
            return new ModelChatResponse("Local model is unavailable right now. Check Ollama logs and try again.", null, true);
        }
    }

    private async Task<float[]> ReadEmbeddingResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("embeddings", out var embeddingsElement)
            && embeddingsElement.ValueKind == JsonValueKind.Array
            && embeddingsElement.GetArrayLength() > 0)
        {
            var first = embeddingsElement[0];
            if (first.ValueKind == JsonValueKind.Array)
            {
                return first.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            }
        }

        if (document.RootElement.TryGetProperty("embedding", out var embeddingElement)
            && embeddingElement.ValueKind == JsonValueKind.Array)
        {
            return embeddingElement.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        }

        return [];
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(OllamaModelGateway));
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    private static string NormalizeChatRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            _ => "user"
        };
    }

    private static string BuildFallback(ModelPrompt prompt)
    {
        var preview = prompt.UserPrompt.Length > 420 ? prompt.UserPrompt[..420] : prompt.UserPrompt;
        return $"Fallback output for {prompt.Role}: {preview}";
    }

    private static float[] BuildFallbackEmbedding(string text)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var vector = new float[32];
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = hash[index] / 255f;
        }

        return vector;
    }

    private static string Truncate(string value, int limit)
    {
        return value.Length > limit ? value[..limit] : value;
    }
}

public sealed class GitHubCatalogService : IGitHubCatalog
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubCatalogService> _logger;

    public GitHubCatalogService(IHttpClientFactory httpClientFactory, IOptions<GitHubOptions> options, ILogger<GitHubCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RepositoryRef>> ListRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            if (!string.IsNullOrWhiteSpace(_options.RepositoryOwner) && !string.IsNullOrWhiteSpace(_options.RepositoryName))
            {
                return
                [
                    new RepositoryRef
                    {
                        Owner = _options.RepositoryOwner,
                        Name = _options.RepositoryName,
                        FullName = $"{_options.RepositoryOwner}/{_options.RepositoryName}",
                        DefaultBranch = "main"
                    }
                ];
            }

            return [];
        }

        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var repositories = new List<RepositoryRef>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var owner = item.TryGetProperty("owner", out var ownerElement) && ownerElement.TryGetProperty("login", out var loginElement)
                    ? loginElement.GetString() ?? string.Empty
                    : string.Empty;
                var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                repositories.Add(new RepositoryRef
                {
                    Owner = owner,
                    Name = name,
                    FullName = item.TryGetProperty("full_name", out var fullNameElement) ? fullNameElement.GetString() ?? $"{owner}/{name}" : $"{owner}/{name}",
                    DefaultBranch = item.TryGetProperty("default_branch", out var defaultBranchElement) ? defaultBranchElement.GetString() ?? string.Empty : string.Empty
                });
            }

            return repositories;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to list GitHub repositories.");
            return [];
        }
    }

    public async Task<IReadOnlyList<SprintRef>> ListMilestonesAsync(string owner, string repository, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return [];
        }

        try
        {
            var client = CreateClient();
            var response = await client.GetAsync($"/repos/{owner}/{repository}/milestones?state=all&per_page=100", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var sprints = new List<SprintRef>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                DateTimeOffset? dueOn = item.TryGetProperty("due_on", out var dueOnElement) && dueOnElement.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(dueOnElement.GetString(), out var parsedDueOn) ? parsedDueOn : null;

                sprints.Add(new SprintRef
                {
                    Id = item.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) ? id : 0,
                    Title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty,
                    Number = item.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var number) ? number : 0,
                    State = item.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? string.Empty : string.Empty,
                    DueOn = dueOn
                });
            }

            return sprints;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to list GitHub milestones for {Owner}/{Repository}.", owner, repository);
            return [];
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(GitHubCatalogService));
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApexAgentTeam", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        return client;
    }
}

public sealed class GitHubIssueSink : IExternalTaskSink
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubIssueSink> _logger;

    public GitHubIssueSink(IHttpClientFactory httpClientFactory, IOptions<GitHubOptions> options, ILogger<GitHubIssueSink> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExternalTaskRef> CreateTaskAsync(ExternalTaskDraft draft, CancellationToken cancellationToken)
    {
        var owner = string.IsNullOrWhiteSpace(draft.RepositoryOwner) ? _options.RepositoryOwner : draft.RepositoryOwner;
        var repository = string.IsNullOrWhiteSpace(draft.RepositoryName) ? _options.RepositoryName : draft.RepositoryName;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return new ExternalTaskRef
            {
                Provider = "github",
                ExternalId = "local-disabled",
                Title = draft.Title,
                Status = "Skipped",
                Url = null
            };
        }

        var client = CreateClient();
        var response = await client.PostAsJsonAsync($"/repos/{owner}/{repository}/issues", new
        {
            title = draft.Title,
            body = draft.Body,
            labels = draft.Labels
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("GitHub issue creation failed: {StatusCode} {Body}", response.StatusCode, body);
            return new ExternalTaskRef
            {
                Provider = "github",
                ExternalId = "request-failed",
                Title = draft.Title,
                Status = response.StatusCode.ToString(),
                Url = null
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return new ExternalTaskRef
        {
            Provider = "github",
            ExternalId = document.RootElement.GetProperty("number").GetInt32().ToString(),
            Title = draft.Title,
            Url = document.RootElement.GetProperty("html_url").GetString(),
            Status = "Created"
        };
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(GitHubIssueSink));
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApexAgentTeam", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        return client;
    }
}

public sealed class GitWorkspaceToolset : IWorkspaceToolset
{
    private static readonly string[] PreviewExtensions = [".cs", ".md", ".json", ".ts", ".tsx", ".css", ".csproj", ".sln"];
    private static readonly string[] IgnoredSegments = [".git", "node_modules", "bin", "obj", ".nuget", ".dotnet-home"];

    private readonly WorkspaceOptions _options;
    private readonly IHostEnvironment _environment;

    public GitWorkspaceToolset(IOptions<WorkspaceOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public async Task<WorkspaceSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken)
    {
        var root = GetWorkspaceRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IgnoredSegments.Any(segment => path.Contains($"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Take(160)
            .ToList();

        var previews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in files.Where(path => PreviewExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).Take(24))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
                previews[relativePath] = text.Length > 1_600 ? text[..1_600] : text;
            }
            catch
            {
                previews[relativePath] = string.Empty;
            }
        }

        return new WorkspaceSnapshot(root, files, previews);
    }

    public Task<PatchApplyResult> ApplyPatchAsync(PatchProposal proposal, CancellationToken cancellationToken)
    {
        return ApplyInternalAsync(proposal.Diff, reverse: false, cancellationToken);
    }

    public Task<PatchApplyResult> RevertPatchAsync(PatchProposal proposal, CancellationToken cancellationToken)
    {
        return ApplyInternalAsync(proposal.Diff, reverse: true, cancellationToken);
    }

    public async Task<TestRunResult> RunValidationAsync(CancellationToken cancellationToken)
    {
        var result = await RunShellAsync(_options.ValidationCommand, GetWorkspaceRoot(), cancellationToken);
        return new TestRunResult(result.ExitCode == 0, $"{result.StdOut}\n{result.StdErr}".Trim());
    }

    private async Task<PatchApplyResult> ApplyInternalAsync(string diff, bool reverse, CancellationToken cancellationToken)
    {
        var root = GetWorkspaceRoot();
        var patchFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(patchFile, diff, cancellationToken);

        try
        {
            var args = reverse
                ? $"-c safe.directory=\"{root}\" apply -R --whitespace=nowarn \"{patchFile}\""
                : $"-c safe.directory=\"{root}\" apply --whitespace=nowarn \"{patchFile}\"";
            var result = await RunProcessAsync("git", args, root, cancellationToken);
            return new PatchApplyResult(result.ExitCode == 0, result.StdOut, result.StdErr);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }

    private string GetWorkspaceRoot()
    {
        return Path.GetFullPath(_options.RootPath, _environment.ContentRootPath);
    }

    private static Task<ProcessResult> RunShellAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunProcessAsync("powershell", $"-NoProfile -Command \"{command}\"", workingDirectory, cancellationToken);
        }

        return RunProcessAsync("/bin/bash", $"-lc \"{command}\"", workingDirectory, cancellationToken);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

public sealed class UnifiedDiffPatchPolicy : IPatchPolicy
{
    private static readonly string[] BlockedFragments = ["/.git/", "\\.git\\", ".git", ".env", "node_modules", ".nuget", ".dotnet-home", "/bin/", "/obj/"];

    public PatchPolicyDecision Evaluate(PatchProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.Diff))
        {
            return new PatchPolicyDecision(false, "Patch diff is empty.");
        }

        if (proposal.Diff.Length > 200_000)
        {
            return new PatchPolicyDecision(false, "Patch diff is too large for V2 sandbox rules.");
        }

        if (!proposal.Diff.Contains("diff --git", StringComparison.Ordinal) && !proposal.Diff.Contains("--- ", StringComparison.Ordinal))
        {
            return new PatchPolicyDecision(false, "Patch must be a unified diff.");
        }

        if (proposal.TargetPaths.Any(path => path.Contains("..", StringComparison.Ordinal)))
        {
            return new PatchPolicyDecision(false, "Patch paths must stay inside the workspace.");
        }

        if (proposal.TargetPaths.Any(path => BlockedFragments.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
        {
            return new PatchPolicyDecision(false, "Patch targets a protected path.");
        }

        return new PatchPolicyDecision(true, "Patch accepted by sandbox policy.");
    }
}

