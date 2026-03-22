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
                DateTimeOffset? modifiedAt = null;
                if (item.TryGetProperty("modified_at", out var modifiedElement)
                    && modifiedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(modifiedElement.GetString(), out var parsedModifiedAt))
                {
                    modifiedAt = parsedModifiedAt;
                }

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
    private readonly TimeProvider _timeProvider;

    public GitHubCatalogService(IHttpClientFactory httpClientFactory, IOptions<GitHubOptions> options, ILogger<GitHubCatalogService> logger, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
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
            var client = CreateRestClient(nameof(GitHubCatalogService));
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
            var client = CreateRestClient(nameof(GitHubCatalogService));
            var response = await client.GetAsync($"/repos/{owner}/{repository}/milestones?state=all&per_page=100", cancellationToken);
            response.EnsureSuccessStatusCode();

            var sprints = await ReadMilestonesAsync(response, cancellationToken);
            if (sprints.Count > 0)
            {
                return sprints;
            }

            return await EnsureDefaultMilestonesAsync(owner, repository, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to list GitHub milestones for {Owner}/{Repository}.", owner, repository);
            return [];
        }
    }

    public async Task<GitHubBoardSnapshot> GetRepositoryBoardAsync(string owner, string repository, CancellationToken cancellationToken)
    {
        var repositoryRef = new RepositoryRef
        {
            Owner = owner,
            Name = repository,
            FullName = $"{owner}/{repository}",
            DefaultBranch = "main"
        };

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return new GitHubBoardSnapshot
            {
                Repository = repositoryRef,
                Source = "empty",
                StatusMessage = "Repository secimi eksik."
            };
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            try
            {
                var board = await LoadProjectBoardAsync(repositoryRef, cancellationToken);
                if (board.Items.Count > 0 || board.Sprints.Count > 0)
                {
                    return board;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Unable to load GitHub project board for {Owner}/{Repository}.", owner, repository);
            }
        }

        try
        {
            var fallbackBoard = await LoadMilestoneBoardAsync(repositoryRef, cancellationToken);
            if (fallbackBoard.Items.Count > 0 || fallbackBoard.Sprints.Count > 0)
            {
                return fallbackBoard;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load GitHub milestone board for {Owner}/{Repository}.", owner, repository);
        }

        return new GitHubBoardSnapshot
        {
            Repository = repositoryRef,
            Source = string.IsNullOrWhiteSpace(_options.AccessToken) ? "token-missing" : "empty",
            StatusMessage = string.IsNullOrWhiteSpace(_options.AccessToken)
                ? "Project board verisi icin GitHub token gerekli. Public milestone fallback da sonuc donmedi."
                : "Secilen repository icin iteration veya issue bulunamadi."
        };
    }

    public async Task<PullRequestRef?> CreatePullRequestAsync(Mission mission, WorkspaceBranchResult branchResult, CancellationToken cancellationToken)
    {
        if (!branchResult.Success
            || mission.SelectedRepository is null
            || string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return null;
        }

        var repository = mission.SelectedRepository;
        var body = BuildPullRequestBody(mission, branchResult);
        var client = CreateRestClient(nameof(GitHubCatalogService));
        var response = await client.PostAsJsonAsync($"/repos/{repository.Owner}/{repository.Name}/pulls", new
        {
            title = mission.Title,
            head = branchResult.BranchName,
            @base = branchResult.BaseBranch,
            body
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("GitHub pull request creation failed for {Owner}/{Repository}: {StatusCode} {Body}", repository.Owner, repository.Name, response.StatusCode, raw);
            return new PullRequestRef
            {
                Provider = "github",
                ExternalId = "request-failed",
                Title = mission.Title,
                Status = response.StatusCode.ToString(),
                HeadBranch = branchResult.BranchName,
                BaseBranch = branchResult.BaseBranch
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var number = document.RootElement.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var parsedNumber)
            ? parsedNumber.ToString()
            : string.Empty;

        return new PullRequestRef
        {
            Provider = "github",
            ExternalId = number,
            Title = document.RootElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? mission.Title : mission.Title,
            Url = document.RootElement.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null,
            Status = "Created",
            HeadBranch = branchResult.BranchName,
            BaseBranch = branchResult.BaseBranch
        };
    }

    public async Task<IReadOnlyList<SprintRef>> EnsureDefaultMilestonesAsync(string owner, string repository, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return [];
        }

        try
        {
            var client = CreateRestClient(nameof(GitHubCatalogService));
            var existingResponse = await client.GetAsync($"/repos/{owner}/{repository}/milestones?state=all&per_page=100", cancellationToken);
            existingResponse.EnsureSuccessStatusCode();
            var existingMilestones = await ReadMilestonesAsync(existingResponse, cancellationToken);
            if (existingMilestones.Count > 0)
            {
                return existingMilestones;
            }

            var blueprints = BuildDefaultMilestones();
            foreach (var blueprint in blueprints)
            {
                var createResponse = await client.PostAsJsonAsync($"/repos/{owner}/{repository}/milestones", new
                {
                    title = blueprint.Title,
                    description = blueprint.Description,
                    state = "open",
                    due_on = blueprint.DueOn?.UtcDateTime.ToString("O")
                }, cancellationToken);

                if (!createResponse.IsSuccessStatusCode)
                {
                    var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("GitHub default milestone creation failed for {Owner}/{Repository}: {StatusCode} {Body}", owner, repository, createResponse.StatusCode, body);
                }
            }

            var refreshResponse = await client.GetAsync($"/repos/{owner}/{repository}/milestones?state=all&per_page=100", cancellationToken);
            refreshResponse.EnsureSuccessStatusCode();
            return await ReadMilestonesAsync(refreshResponse, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to create default milestones for {Owner}/{Repository}.", owner, repository);
            return [];
        }
    }

    private async Task<List<SprintRef>> ReadMilestonesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
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
                Id = item.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) ? id.ToString() : string.Empty,
                Title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty,
                Number = item.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var number) ? number : 0,
                State = item.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? string.Empty : string.Empty,
                DueOn = dueOn
            });
        }

        return sprints;
    }

    private async Task<GitHubBoardSnapshot> LoadProjectBoardAsync(RepositoryRef repository, CancellationToken cancellationToken)
    {
        var candidates = await ListCandidateProjectsAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return new GitHubBoardSnapshot
            {
                Repository = repository,
                Source = "project-v2",
                StatusMessage = "Erisilebilir project bulunamadi."
            };
        }

        var matchedProjects = new List<GitHubProjectRef>();
        var allItems = new List<GitHubBoardItemRef>();

        foreach (var candidate in candidates)
        {
            var projectItems = await LoadProjectItemsAsync(candidate, repository, cancellationToken);
            if (projectItems.Count == 0)
            {
                continue;
            }

            matchedProjects.Add(new GitHubProjectRef
            {
                Id = candidate.Id,
                Number = candidate.Number,
                Title = candidate.Title,
                Url = candidate.Url,
                OwnerLogin = candidate.OwnerLogin,
                OwnerType = candidate.OwnerType,
                ShortDescription = candidate.ShortDescription,
                Closed = candidate.Closed
            });

            allItems.AddRange(projectItems);
        }

        var sprints = BuildSprintCatalog(allItems);
        return new GitHubBoardSnapshot
        {
            Repository = repository,
            Source = "project-v2",
            StatusMessage = allItems.Count > 0
                ? $"{matchedProjects.Count} project ve {sprints.Count} sprint bulundu."
                : "Project board icinde bu repository ile eslesen kart bulunamadi.",
            Projects = matchedProjects,
            Sprints = sprints,
            Items = allItems.OrderBy(item => item.ProjectTitle).ThenBy(item => item.Status).ThenByDescending(item => item.UpdatedAt).ToList(),
            Columns = BuildColumns(allItems)
        };
    }

    private async Task<GitHubBoardSnapshot> LoadMilestoneBoardAsync(RepositoryRef repository, CancellationToken cancellationToken)
    {
        var sprints = (await ListMilestonesAsync(repository.Owner, repository.Name, cancellationToken)).ToList();
        var items = new List<GitHubBoardItemRef>();
        foreach (var sprint in sprints)
        {
            items.AddRange(await ListMilestoneItemsAsync(repository, sprint, cancellationToken));
        }

        return new GitHubBoardSnapshot
        {
            Repository = repository,
            Source = "milestones",
            StatusMessage = sprints.Count > 0
                ? $"{sprints.Count} milestone sprint ve {items.Count} issue bulundu."
                : "Milestone tabanli sprint bulunamadi.",
            Projects =
            [
                new GitHubProjectRef
                {
                    Id = $"milestones:{repository.FullName}",
                    Number = 0,
                    Title = $"{repository.FullName} milestones",
                    Url = $"{_options.WebUrl}/{repository.FullName}/milestones",
                    OwnerLogin = repository.Owner,
                    OwnerType = "Repository",
                    ShortDescription = "REST milestone fallback",
                    Closed = false
                }
            ],
            Sprints = sprints,
            Items = items,
            Columns = BuildColumns(items)
        };
    }

    private async Task<List<ProjectCandidate>> ListCandidateProjectsAsync(CancellationToken cancellationToken)
    {
        const string query = """
            query {
              viewer {
                login
                projectsV2(first: 25) {
                  nodes {
                    id
                    number
                    title
                    shortDescription
                    url
                    closed
                  }
                }
                organizations(first: 20) {
                  nodes {
                    login
                    projectsV2(first: 15) {
                      nodes {
                        id
                        number
                        title
                        shortDescription
                        url
                        closed
                      }
                    }
                  }
                }
              }
            }
            """;

        using var document = await ExecuteGraphQlAsync(query, null, cancellationToken);
        if (document is null)
        {
            return [];
        }

        if (!document.RootElement.TryGetProperty("data", out var dataElement)
            || !dataElement.TryGetProperty("viewer", out var viewerElement))
        {
            return [];
        }

        var projects = new List<ProjectCandidate>();
        var viewerLogin = viewerElement.TryGetProperty("login", out var loginElement) ? loginElement.GetString() ?? string.Empty : string.Empty;
        if (viewerElement.TryGetProperty("projectsV2", out var viewerProjectsElement)
            && viewerProjectsElement.TryGetProperty("nodes", out var viewerNodes)
            && viewerNodes.ValueKind == JsonValueKind.Array)
        {
            projects.AddRange(ReadProjectCandidates(viewerNodes, viewerLogin, "User"));
        }

        if (viewerElement.TryGetProperty("organizations", out var organizationsElement)
            && organizationsElement.TryGetProperty("nodes", out var organizationNodes)
            && organizationNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var organization in organizationNodes.EnumerateArray())
            {
                var organizationLogin = organization.TryGetProperty("login", out var organizationLoginElement)
                    ? organizationLoginElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!organization.TryGetProperty("projectsV2", out var organizationProjects)
                    || !organizationProjects.TryGetProperty("nodes", out var organizationProjectNodes)
                    || organizationProjectNodes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                projects.AddRange(ReadProjectCandidates(organizationProjectNodes, organizationLogin, "Organization"));
            }
        }

        return projects
            .GroupBy(project => project.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<List<GitHubBoardItemRef>> LoadProjectItemsAsync(ProjectCandidate candidate, RepositoryRef repository, CancellationToken cancellationToken)
    {
        const string query = """
            query($projectId: ID!, $after: String) {
              node(id: $projectId) {
                ... on ProjectV2 {
                  items(first: 100, after: $after) {
                    pageInfo {
                      hasNextPage
                      endCursor
                    }
                    nodes {
                      id
                      isArchived
                      updatedAt
                      content {
                        __typename
                        ... on DraftIssue {
                          title
                          body
                        }
                        ... on Issue {
                          number
                          title
                          body
                          state
                          url
                          repository {
                            name
                            nameWithOwner
                            owner {
                              login
                            }
                          }
                          labels(first: 12) {
                            nodes {
                              name
                            }
                          }
                          assignees(first: 12) {
                            nodes {
                              login
                            }
                          }
                        }
                        ... on PullRequest {
                          number
                          title
                          body
                          state
                          url
                          repository {
                            name
                            nameWithOwner
                            owner {
                              login
                            }
                          }
                          labels(first: 12) {
                            nodes {
                              name
                            }
                          }
                          assignees(first: 12) {
                            nodes {
                              login
                            }
                          }
                        }
                      }
                      status: fieldValueByName(name: "Status") {
                        ... on ProjectV2ItemFieldSingleSelectValue {
                          name
                          optionId
                        }
                      }
                      iteration: fieldValueByName(name: "Iteration") {
                        ... on ProjectV2ItemFieldIterationValue {
                          iterationId
                          startDate
                          duration
                          field {
                            ... on ProjectV2IterationField {
                              configuration {
                                iterations {
                                  id
                                  title
                                }
                                completedIterations {
                                  id
                                  title
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        var rawItems = new List<GitHubBoardItemRef>();
        string? cursor = null;

        do
        {
            using var document = await ExecuteGraphQlAsync(query, new { projectId = candidate.Id, after = cursor }, cancellationToken);
            if (document is null)
            {
                break;
            }

            if (!document.RootElement.TryGetProperty("data", out var dataElement)
                || !dataElement.TryGetProperty("node", out var nodeElement)
                || nodeElement.ValueKind != JsonValueKind.Object
                || !nodeElement.TryGetProperty("items", out var itemsElement)
                || !itemsElement.TryGetProperty("nodes", out var itemNodes)
                || itemNodes.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in itemNodes.EnumerateArray())
            {
                var parsed = ParseProjectItem(candidate, repository, item);
                if (parsed is not null)
                {
                    rawItems.Add(parsed);
                }
            }

            cursor = null;
            if (itemsElement.TryGetProperty("pageInfo", out var pageInfo)
                && pageInfo.TryGetProperty("hasNextPage", out var hasNextPageElement)
                && hasNextPageElement.ValueKind == JsonValueKind.True
                && pageInfo.TryGetProperty("endCursor", out var endCursorElement))
            {
                cursor = endCursorElement.GetString();
            }
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        var hasRepositoryBoundItems = rawItems.Any(item =>
            string.Equals(item.RepositoryFullName, repository.FullName, StringComparison.OrdinalIgnoreCase));

        if (!hasRepositoryBoundItems)
        {
            return [];
        }

        foreach (var item in rawItems.Where(item => string.IsNullOrWhiteSpace(item.RepositoryFullName)))
        {
            item.RepositoryOwner = repository.Owner;
            item.RepositoryName = repository.Name;
            item.RepositoryFullName = repository.FullName;
        }

        return rawItems
            .Where(item => string.IsNullOrWhiteSpace(item.RepositoryFullName)
                || string.Equals(item.RepositoryFullName, repository.FullName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private GitHubBoardItemRef? ParseProjectItem(ProjectCandidate candidate, RepositoryRef repository, JsonElement item)
    {
        if (item.TryGetProperty("isArchived", out var archivedElement) && archivedElement.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var contentType = content.TryGetProperty("__typename", out var contentTypeElement)
            ? contentTypeElement.GetString() ?? "Unknown"
            : "Unknown";
        var repositoryFullName = ReadRepositoryFullName(content);
        if (!string.IsNullOrWhiteSpace(repositoryFullName)
            && !string.Equals(repositoryFullName, repository.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var status = item.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.Object
            && statusElement.TryGetProperty("name", out var statusNameElement)
            ? statusNameElement.GetString() ?? "Backlog"
            : "Backlog";
        var statusOptionId = item.TryGetProperty("status", out var statusValue)
            && statusValue.ValueKind == JsonValueKind.Object
            && statusValue.TryGetProperty("optionId", out var optionIdElement)
            ? optionIdElement.GetString()
            : null;

        var iterationContext = ReadIterationContext(item.TryGetProperty("iteration", out var iterationElement) ? iterationElement : default);
        var sprintId = string.IsNullOrWhiteSpace(iterationContext.IterationId)
            ? $"{candidate.Id}:unscheduled"
            : $"{candidate.Id}:{iterationContext.IterationId}";

        var updatedAt = item.TryGetProperty("updatedAt", out var updatedAtElement)
            && updatedAtElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedAtElement.GetString(), out var parsedUpdatedAt)
            ? parsedUpdatedAt
            : _timeProvider.GetUtcNow();

        return new GitHubBoardItemRef
        {
            Id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
            ProjectId = candidate.Id,
            ProjectNumber = candidate.Number,
            ProjectTitle = candidate.Title,
            ProjectUrl = candidate.Url,
            SprintId = sprintId,
            IterationId = iterationContext.IterationId,
            SprintTitle = string.IsNullOrWhiteSpace(iterationContext.Title) ? "No iteration" : iterationContext.Title!,
            Status = status,
            StatusOptionId = statusOptionId,
            ContentType = contentType,
            Number = content.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var parsedNumber) ? parsedNumber : null,
            Title = content.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty,
            Description = content.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty,
            State = content.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? "OPEN" : "OPEN",
            Url = content.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null,
            RepositoryOwner = content.TryGetProperty("repository", out var repositoryElement)
                && repositoryElement.TryGetProperty("owner", out var ownerElement)
                && ownerElement.TryGetProperty("login", out var ownerLoginElement)
                ? ownerLoginElement.GetString() ?? string.Empty
                : string.Empty,
            RepositoryName = content.TryGetProperty("repository", out var nameElement)
                && nameElement.TryGetProperty("name", out var repositoryNameElement)
                ? repositoryNameElement.GetString() ?? string.Empty
                : string.Empty,
            RepositoryFullName = repositoryFullName ?? string.Empty,
            Labels = ReadStringList(content, "labels", "nodes", "name"),
            Assignees = ReadStringList(content, "assignees", "nodes", "login"),
            Subtasks = ExtractChecklistItems(content.TryGetProperty("body", out var bodyForTasks) ? bodyForTasks.GetString() ?? string.Empty : string.Empty),
            UpdatedAt = updatedAt,
            IsDraft = string.Equals(contentType, "DraftIssue", StringComparison.Ordinal)
        };
    }

    private IterationContext ReadIterationContext(JsonElement iterationElement)
    {
        if (iterationElement.ValueKind != JsonValueKind.Object)
        {
            return new IterationContext(null, null);
        }

        var iterationId = iterationElement.TryGetProperty("iterationId", out var iterationIdElement)
            ? iterationIdElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(iterationId))
        {
            return new IterationContext(null, null);
        }

        if (iterationElement.TryGetProperty("field", out var fieldElement)
            && fieldElement.ValueKind == JsonValueKind.Object
            && fieldElement.TryGetProperty("configuration", out var configurationElement)
            && configurationElement.ValueKind == JsonValueKind.Object)
        {
            return new IterationContext(iterationId, ReadIterationTitle(configurationElement, iterationId));
        }

        return new IterationContext(iterationId, null);
    }

    private static string ReadIterationTitle(JsonElement configurationElement, string iterationId)
    {
        foreach (var propertyName in new[] { "iterations", "completedIterations" })
        {
            if (!configurationElement.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var iteration in items.EnumerateArray())
            {
                if (iteration.TryGetProperty("id", out var idElement)
                    && string.Equals(idElement.GetString(), iterationId, StringComparison.Ordinal))
                {
                    return iteration.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private async Task<List<GitHubBoardItemRef>> ListMilestoneItemsAsync(RepositoryRef repository, SprintRef sprint, CancellationToken cancellationToken)
    {
        var client = CreateRestClient(nameof(GitHubCatalogService));
        var milestoneNumber = sprint.Number <= 0 ? "none" : sprint.Number.ToString();
        var response = await client.GetAsync($"/repos/{repository.Owner}/{repository.Name}/issues?state=all&milestone={milestoneNumber}&per_page=100", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<GitHubBoardItemRef>();
        foreach (var issue in document.RootElement.EnumerateArray())
        {
            if (issue.TryGetProperty("pull_request", out _))
            {
                continue;
            }

            var body = issue.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
            items.Add(new GitHubBoardItemRef
            {
                Id = issue.TryGetProperty("node_id", out var nodeIdElement) ? nodeIdElement.GetString() ?? string.Empty : string.Empty,
                ProjectId = $"milestones:{repository.FullName}",
                ProjectNumber = 0,
                ProjectTitle = $"{repository.FullName} milestones",
                ProjectUrl = $"{_options.WebUrl}/{repository.FullName}/milestones",
                SprintId = sprint.Id,
                IterationId = sprint.Id,
                SprintTitle = sprint.Title,
                Status = issue.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? "open" : "open",
                ContentType = "Issue",
                Number = issue.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var parsedNumber) ? parsedNumber : null,
                Title = issue.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty,
                Description = body,
                State = issue.TryGetProperty("state", out var issueStateElement) ? issueStateElement.GetString() ?? "open" : "open",
                Url = issue.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null,
                RepositoryOwner = repository.Owner,
                RepositoryName = repository.Name,
                RepositoryFullName = repository.FullName,
                Labels = ReadNamedChildren(issue, "labels", "name"),
                Assignees = ReadNamedChildren(issue, "assignees", "login"),
                Subtasks = ExtractChecklistItems(body),
                UpdatedAt = issue.TryGetProperty("updated_at", out var updatedAtElement)
                    && updatedAtElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(updatedAtElement.GetString(), out var parsedUpdatedAt)
                    ? parsedUpdatedAt
                    : _timeProvider.GetUtcNow()
            });
        }

        return items;
    }

    private static List<SprintRef> BuildSprintCatalog(IReadOnlyCollection<GitHubBoardItemRef> items)
    {
        return items
            .GroupBy(item => item.SprintId, StringComparer.Ordinal)
            .Select((group, index) =>
            {
                var first = group.First();
                return new SprintRef
                {
                    Id = first.SprintId,
                    Title = first.SprintTitle,
                    Number = index + 1,
                    State = string.Equals(first.SprintTitle, "No iteration", StringComparison.OrdinalIgnoreCase) ? "unscheduled" : "scheduled",
                    ProjectId = first.ProjectId,
                    ProjectNumber = first.ProjectNumber,
                    ProjectTitle = first.ProjectTitle,
                    IterationId = first.IterationId,
                    DueOn = null
                };
            })
            .OrderBy(item => item.ProjectTitle)
            .ThenBy(item => item.Title)
            .ToList();
    }

    private static List<GitHubBoardColumn> BuildColumns(IReadOnlyCollection<GitHubBoardItemRef> items)
    {
        return items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Backlog" : item.Status, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GitHubBoardColumn
            {
                Id = group.Key.ToLowerInvariant().Replace(' ', '-'),
                Title = group.Key,
                Items = group.OrderByDescending(item => item.UpdatedAt).ToList()
            })
            .ToList();
    }

    private static List<ProjectCandidate> ReadProjectCandidates(JsonElement nodes, string ownerLogin, string ownerType)
    {
        var candidates = new List<ProjectCandidate>();
        foreach (var node in nodes.EnumerateArray())
        {
            candidates.Add(new ProjectCandidate(
                node.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
                node.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var number) ? number : 0,
                node.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty,
                node.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty,
                ownerLogin,
                ownerType,
                node.TryGetProperty("shortDescription", out var descriptionElement) ? descriptionElement.GetString() : null,
                node.TryGetProperty("closed", out var closedElement) && closedElement.ValueKind == JsonValueKind.True));
        }

        return candidates;
    }

    private async Task<JsonDocument?> ExecuteGraphQlAsync(string query, object? variables, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return null;
        }

        var client = CreateGraphQlClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
        request.Content = JsonContent.Create(new { query, variables });
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("GitHub GraphQL request failed: {StatusCode} {Body}", response.StatusCode, raw);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            _logger.LogWarning("GitHub GraphQL request returned errors: {Errors}", errorsElement.ToString());
            document.Dispose();
            return null;
        }

        return document;
    }

    private List<(string Title, string Description, DateTimeOffset? DueOn)> BuildDefaultMilestones()
    {
        var now = _timeProvider.GetUtcNow();
        return
        [
            ($"Sprint 1 - Foundation", "Repository setup, architecture pass, and baseline backlog.", now.AddDays(14)),
            ($"Sprint 2 - Build", "Core implementation, API/UI integration, and validation.", now.AddDays(28)),
            ($"Sprint 3 - Polish", "Stability, QA, rollout prep, and support handoff.", now.AddDays(42))
        ];
    }

    private static string? ReadRepositoryFullName(JsonElement content)
    {
        return content.TryGetProperty("repository", out var repositoryElement)
            && repositoryElement.TryGetProperty("nameWithOwner", out var nameWithOwnerElement)
            ? nameWithOwnerElement.GetString()
            : null;
    }

    private static List<string> ReadStringList(JsonElement root, string propertyName, string nodesPropertyName, string valuePropertyName)
    {
        if (!root.TryGetProperty(propertyName, out var propertyElement)
            || propertyElement.ValueKind != JsonValueKind.Object
            || !propertyElement.TryGetProperty(nodesPropertyName, out var nodesElement)
            || nodesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodesElement.EnumerateArray()
            .Select(node => node.TryGetProperty(valuePropertyName, out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ReadNamedChildren(JsonElement root, string propertyName, string valuePropertyName)
    {
        if (!root.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return propertyElement.EnumerateArray()
            .Select(node => node.TryGetProperty(valuePropertyName, out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractChecklistItems(string body)
    {
        return body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("* [ ] ", StringComparison.OrdinalIgnoreCase))
            .Select(line => line[6..].Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private string BuildPullRequestBody(Mission mission, WorkspaceBranchResult branchResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Apex Agent Team");
        builder.AppendLine();
        builder.AppendLine($"- Mission: {mission.Title}");
        builder.AppendLine($"- Branch: `{branchResult.BranchName}`");
        builder.AppendLine($"- Base: `{branchResult.BaseBranch}`");

        if (mission.SelectedSprint is not null)
        {
            builder.AppendLine($"- Sprint: {mission.SelectedSprint.Title}");
        }

        if (mission.SelectedWorkItem is not null)
        {
            builder.AppendLine($"- Source task: {mission.SelectedWorkItem.Title}");
            if (mission.SelectedWorkItem.Number is int issueNumber && string.Equals(mission.SelectedWorkItem.ContentType, "Issue", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine();
                builder.AppendLine($"Closes #{issueNumber}");
            }
        }

        if (mission.Artifacts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Agent Notes");
            foreach (var artifact in mission.Artifacts.Take(4))
            {
                builder.AppendLine();
                builder.AppendLine($"### {artifact.Key}");
                builder.AppendLine(artifact.Value.Length > 900 ? artifact.Value[..900] : artifact.Value);
            }
        }

        return builder.ToString().Trim();
    }

    private HttpClient CreateRestClient(string name)
    {
        var client = _httpClientFactory.CreateClient(name);
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApexAgentTeam", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        return client;
    }

    private HttpClient CreateGraphQlClient()
    {
        var client = _httpClientFactory.CreateClient($"{nameof(GitHubCatalogService)}.GraphQl");
        client.BaseAddress = new Uri(_options.GraphQlUrl);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApexAgentTeam", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        return client;
    }

    private sealed record ProjectCandidate(string Id, int Number, string Title, string Url, string OwnerLogin, string OwnerType, string? ShortDescription, bool Closed);

    private sealed record IterationContext(string? IterationId, string? Title);
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

public sealed partial class GitWorkspaceToolset : IWorkspaceToolset
{
    private static readonly string[] PreviewExtensions = [".cs", ".md", ".json", ".ts", ".tsx", ".css", ".csproj", ".sln"];
    private static readonly string[] IgnoredSegments = [".git", "node_modules", "bin", "obj", ".nuget", ".dotnet-home"];

    private readonly WorkspaceOptions _options;
    private readonly GitHubOptions _gitHubOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GitWorkspaceToolset> _logger;

    public GitWorkspaceToolset(IOptions<WorkspaceOptions> options, IOptions<GitHubOptions> gitHubOptions, IHostEnvironment environment, ILogger<GitWorkspaceToolset> logger)
    {
        _options = options.Value;
        _gitHubOptions = gitHubOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<WorkspaceSnapshot> CaptureSnapshotAsync(Mission mission, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
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

    public async Task<PatchApplyResult> ApplyPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        return await ApplyInternalAsync(root, proposal.Diff, reverse: false, cancellationToken);
    }

    public async Task<PatchApplyResult> RevertPatchAsync(Mission mission, PatchProposal proposal, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        return await ApplyInternalAsync(root, proposal.Diff, reverse: true, cancellationToken);
    }

    public async Task<TestRunResult> RunValidationAsync(Mission mission, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var command = IsPrimaryWorkspace(root) ? _options.ValidationCommand : _options.RepositoryValidationCommand;
        var result = await RunShellAsync(command, root, cancellationToken);
        return new TestRunResult(result.ExitCode == 0, $"{result.StdOut}\n{result.StdErr}".Trim());
    }

    public async Task<WorkspaceBranchResult> PublishBranchAsync(Mission mission, CancellationToken cancellationToken)
    {
        if (mission.SelectedRepository is null)
        {
            return new WorkspaceBranchResult(false, string.Empty, string.Empty, "Selected repository is required for PR creation.", string.Empty);
        }

        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var branchName = BuildBranchName(mission);
        var baseBranch = string.IsNullOrWhiteSpace(mission.SelectedRepository.DefaultBranch) ? "main" : mission.SelectedRepository.DefaultBranch;

        await RunProcessAsync("git", $"config user.name \"Apex Agent Team\"", root, cancellationToken);
        await RunProcessAsync("git", $"config user.email \"apex-agent-team@local\"", root, cancellationToken);

        var checkoutResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" checkout -B \"{branchName}\" \"{baseBranch}\"", root, cancellationToken);
        if (checkoutResult.ExitCode != 0)
        {
            return new WorkspaceBranchResult(false, branchName, baseBranch, checkoutResult.StdErr, root);
        }

        var statusResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" status --porcelain", root, cancellationToken);
        if (string.IsNullOrWhiteSpace(statusResult.StdOut))
        {
            return new WorkspaceBranchResult(false, branchName, baseBranch, "No repository changes detected for PR creation.", root);
        }

        var addResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" add -A", root, cancellationToken);
        if (addResult.ExitCode != 0)
        {
            return new WorkspaceBranchResult(false, branchName, baseBranch, addResult.StdErr, root);
        }

        var commitMessage = BuildCommitMessage(mission);
        var commitResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" commit -m \"{commitMessage}\"", root, cancellationToken);
        if (commitResult.ExitCode != 0)
        {
            return new WorkspaceBranchResult(false, branchName, baseBranch, $"{commitResult.StdOut}\n{commitResult.StdErr}".Trim(), root);
        }

        var pushResult = await PushBranchAsync(root, mission.SelectedRepository, branchName, cancellationToken);
        return new WorkspaceBranchResult(pushResult.ExitCode == 0, branchName, baseBranch, $"{pushResult.StdOut}\n{pushResult.StdErr}".Trim(), root);
    }

    private async Task<PatchApplyResult> ApplyInternalAsync(string root, string diff, bool reverse, CancellationToken cancellationToken)
    {
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

    private async Task<string> EnsureWorkspaceRootAsync(Mission mission, CancellationToken cancellationToken)
    {
        if (mission.SelectedRepository is null)
        {
            return GetPrimaryWorkspaceRoot();
        }

        var root = GetRepositoryWorkspaceRoot(mission.SelectedRepository);
        var gitDirectory = Path.Combine(root, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            if (string.IsNullOrWhiteSpace(_gitHubOptions.AccessToken))
            {
                throw new InvalidOperationException($"Selected repository '{mission.SelectedRepository.FullName}' is not cloned locally and GitHub token is missing.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            var cloneUrl = BuildCloneUrl(mission.SelectedRepository, authenticated: true);
            var cloneResult = await RunProcessAsync("git", $"clone \"{cloneUrl}\" \"{root}\"", GetRepositoriesRoot(), cancellationToken);
            if (cloneResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Repository clone failed: {cloneResult.StdErr}");
            }
        }

        await TrySyncRepositoryAsync(root, mission.SelectedRepository.DefaultBranch, cancellationToken);
        return root;
    }

    private async Task TrySyncRepositoryAsync(string root, string? baseBranch, CancellationToken cancellationToken)
    {
        var branch = string.IsNullOrWhiteSpace(baseBranch) ? "main" : baseBranch;
        foreach (var arguments in new[]
        {
            $"-c safe.directory=\"{root}\" fetch --all --prune",
            $"-c safe.directory=\"{root}\" checkout \"{branch}\"",
            $"-c safe.directory=\"{root}\" pull --ff-only origin \"{branch}\""
        })
        {
            try
            {
                var result = await RunProcessAsync("git", arguments, root, cancellationToken);
                if (result.ExitCode != 0)
                {
                    _logger.LogDebug("Git sync step failed for {Root}: {Args} => {StdErr}", root, arguments, result.StdErr);
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Git sync step threw for {Root}.", root);
            }
        }
    }

    private async Task<ProcessResult> PushBranchAsync(string root, RepositoryRef repository, string branchName, CancellationToken cancellationToken)
    {
        var remoteUrlResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" remote get-url origin", root, cancellationToken);
        var existingRemoteUrl = remoteUrlResult.ExitCode == 0 ? remoteUrlResult.StdOut.Trim() : string.Empty;
        var authenticatedRemoteUrl = BuildCloneUrl(repository, authenticated: true);
        var shouldSwapRemote = !string.IsNullOrWhiteSpace(_gitHubOptions.AccessToken);

        if (shouldSwapRemote)
        {
            var setRemoteResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" remote set-url origin \"{authenticatedRemoteUrl}\"", root, cancellationToken);
            if (setRemoteResult.ExitCode != 0)
            {
                return setRemoteResult;
            }
        }

        try
        {
            return await RunProcessAsync("git", $"-c safe.directory=\"{root}\" push -u origin \"{branchName}\"", root, cancellationToken);
        }
        finally
        {
            if (shouldSwapRemote && !string.IsNullOrWhiteSpace(existingRemoteUrl))
            {
                await RunProcessAsync("git", $"-c safe.directory=\"{root}\" remote set-url origin \"{existingRemoteUrl}\"", root, cancellationToken);
            }
        }
    }

    private string GetPrimaryWorkspaceRoot()
    {
        return Path.GetFullPath(_options.RootPath, _environment.ContentRootPath);
    }

    private string GetRepositoriesRoot()
    {
        var root = Path.GetFullPath(_options.RepositoriesRootPath, _environment.ContentRootPath);
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetRepositoryWorkspaceRoot(RepositoryRef repository)
    {
        return Path.Combine(GetRepositoriesRoot(), SanitizeSegment(repository.Owner), SanitizeSegment(repository.Name));
    }

    private bool IsPrimaryWorkspace(string root)
    {
        return string.Equals(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
            GetPrimaryWorkspaceRoot().TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private string BuildCloneUrl(RepositoryRef repository, bool authenticated)
    {
        var repositoryUri = new Uri(new Uri(_gitHubOptions.WebUrl.TrimEnd('/') + "/"), $"{repository.Owner}/{repository.Name}.git");
        if (!authenticated || string.IsNullOrWhiteSpace(_gitHubOptions.AccessToken))
        {
            return repositoryUri.ToString();
        }

        var builder = new UriBuilder(repositoryUri)
        {
            UserName = "x-access-token",
            Password = _gitHubOptions.AccessToken
        };

        return builder.Uri.ToString();
    }

    private static string BuildBranchName(Mission mission)
    {
        var slug = SanitizeSegment(mission.Title);
        var shortId = mission.Id.ToString("N")[..8];
        return $"apex/{slug}-{shortId}";
    }

    private static string BuildCommitMessage(Mission mission)
    {
        var title = mission.Title.Trim();
        if (title.Length > 54)
        {
            title = title[..54];
        }

        return $"Apex AI: {title}";
    }

    private static string SanitizeSegment(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            buffer.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var normalized = buffer.ToString().Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "workspace" : normalized;
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


