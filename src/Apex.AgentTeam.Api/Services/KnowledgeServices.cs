using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.Extensions.Options;

namespace Apex.AgentTeam.Api.Services;

public sealed class KnowledgeIngestionHostedService : BackgroundService
{
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<KnowledgeIngestionHostedService> _logger;

    public KnowledgeIngestionHostedService(IMemoryStore memoryStore, ILogger<KnowledgeIngestionHostedService> logger)
    {
        _memoryStore = memoryStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _memoryStore.SeedAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Knowledge ingestion skipped during startup.");
        }
    }
}

public sealed class QdrantMemoryStore : IMemoryStore
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IModelGateway _modelGateway;
    private readonly StorageOptions _storageOptions;
    private readonly WorkspaceOptions _workspaceOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<QdrantMemoryStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _seeded;

    public QdrantMemoryStore(
        IHttpClientFactory httpClientFactory,
        IModelGateway modelGateway,
        IOptions<StorageOptions> storageOptions,
        IOptions<WorkspaceOptions> workspaceOptions,
        IHostEnvironment environment,
        ILogger<QdrantMemoryStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _modelGateway = modelGateway;
        _storageOptions = storageOptions.Value;
        _workspaceOptions = workspaceOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (_seeded)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_seeded)
            {
                return;
            }

            var root = Path.GetFullPath(_workspaceOptions.RootPath, _environment.ContentRootPath);
            var entryPoint = Path.Combine(root, _workspaceOptions.KnowledgeEntryPoint);
            if (!File.Exists(entryPoint))
            {
                _logger.LogWarning("Knowledge entry point was not found: {EntryPoint}", entryPoint);
                return;
            }

            var chunks = MarkdownKnowledgeCrawler.Crawl(entryPoint, root);
            if (chunks.Count == 0)
            {
                return;
            }

            var firstVector = await _modelGateway.EmbedAsync(chunks[0].Content, cancellationToken);
            await EnsureCollectionAsync(firstVector.Length, cancellationToken);

            var client = CreateClient();
            var points = new List<object>();
            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                var vector = index == 0 ? firstVector : await _modelGateway.EmbedAsync(chunk.Content, cancellationToken);
                points.Add(new
                {
                    id = chunk.Id,
                    vector,
                    payload = new
                    {
                        sourcePath = chunk.SourcePath,
                        title = chunk.Title,
                        content = chunk.Content,
                        chunkIndex = chunk.ChunkIndex,
                        links = chunk.Links
                    }
                });
            }

            var response = await client.PutAsJsonAsync($"/collections/{_storageOptions.QdrantCollectionName}/points?wait=true", new { points }, cancellationToken);
            response.EnsureSuccessStatusCode();
            _seeded = true;
            _logger.LogInformation("Seeded {ChunkCount} knowledge chunks into Qdrant.", chunks.Count);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to seed Qdrant knowledge store. The app can still run with empty retrieval.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var vector = await _modelGateway.EmbedAsync(query, cancellationToken);
            var client = CreateClient();
            var response = await client.PostAsJsonAsync($"/collections/{_storageOptions.QdrantCollectionName}/points/search", new
            {
                vector,
                limit,
                with_payload = true
            }, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var results = new List<KnowledgeChunk>();
            if (!document.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in resultElement.EnumerateArray())
            {
                if (!item.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                var chunk = new KnowledgeChunk
                {
                    Id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String && Guid.TryParse(idElement.GetString(), out var id)
                        ? id
                        : Guid.NewGuid(),
                    SourcePath = payload.GetProperty("sourcePath").GetString() ?? string.Empty,
                    Title = payload.GetProperty("title").GetString() ?? string.Empty,
                    Content = payload.GetProperty("content").GetString() ?? string.Empty,
                    ChunkIndex = payload.GetProperty("chunkIndex").GetInt32(),
                    Links = payload.TryGetProperty("links", out var linksElement) && linksElement.ValueKind == JsonValueKind.Array
                        ? linksElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
                        : []
                };

                results.Add(chunk);
            }

            return results;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Knowledge search returned no results for query '{Query}'.", query);
            return [];
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(QdrantMemoryStore));
        client.BaseAddress = new Uri(_storageOptions.QdrantBaseUrl);
        return client;
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var getResponse = await client.GetAsync($"/collections/{_storageOptions.QdrantCollectionName}", cancellationToken);
        if (getResponse.IsSuccessStatusCode)
        {
            var currentVectorSize = await TryReadVectorSizeAsync(getResponse, cancellationToken);
            if (currentVectorSize is null || currentVectorSize == vectorSize)
            {
                return;
            }

            _logger.LogInformation("Recreating Qdrant collection {Collection} because vector size changed from {Current} to {Expected}.", _storageOptions.QdrantCollectionName, currentVectorSize, vectorSize);
            var deleteResponse = await client.DeleteAsync($"/collections/{_storageOptions.QdrantCollectionName}", cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != HttpStatusCode.NotFound)
            {
                deleteResponse.EnsureSuccessStatusCode();
            }
        }
        else if (getResponse.StatusCode != HttpStatusCode.NotFound)
        {
            getResponse.EnsureSuccessStatusCode();
        }

        var payload = new
        {
            vectors = new
            {
                size = vectorSize,
                distance = "Cosine"
            }
        };

        var createResponse = await client.PutAsJsonAsync($"/collections/{_storageOptions.QdrantCollectionName}", payload, cancellationToken);
        if (createResponse.IsSuccessStatusCode || createResponse.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        createResponse.EnsureSuccessStatusCode();
    }

    private static async Task<int?> TryReadVectorSizeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var resultElement)
            || !resultElement.TryGetProperty("config", out var configElement)
            || !configElement.TryGetProperty("params", out var paramsElement)
            || !paramsElement.TryGetProperty("vectors", out var vectorsElement))
        {
            return null;
        }

        if (vectorsElement.ValueKind == JsonValueKind.Object && vectorsElement.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt32(out var directSize))
        {
            return directSize;
        }

        if (vectorsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in vectorsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("size", out var namedSizeElement) && namedSizeElement.TryGetInt32(out var namedSize))
                {
                    return namedSize;
                }
            }
        }

        return null;
    }
}

internal static class MarkdownKnowledgeCrawler
{
    private static readonly Regex LocalLinkRegex = new(@"\[[^\]]+\]\(([^)]+\.md)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<KnowledgeChunk> Crawl(string entryPoint, string repositoryRoot)
    {
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chunks = new List<KnowledgeChunk>();
        queue.Enqueue(Path.GetFullPath(entryPoint));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current) || !File.Exists(current))
            {
                continue;
            }

            var raw = File.ReadAllText(current);
            var relativePath = Path.GetRelativePath(repositoryRoot, current).Replace('\\', '/');
            var links = ExtractLinks(raw, current);
            foreach (var link in links)
            {
                if (File.Exists(link))
                {
                    queue.Enqueue(link);
                }
            }

            chunks.AddRange(SplitIntoChunks(relativePath, raw, links.Select(link => Path.GetRelativePath(repositoryRoot, link).Replace('\\', '/')).ToList()));
        }

        return chunks;
    }

    private static List<string> ExtractLinks(string markdown, string currentFile)
    {
        var links = new List<string>();
        foreach (Match match in LocalLinkRegex.Matches(markdown))
        {
            var target = match.Groups[1].Value.Trim();
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalized = target.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(currentFile)!, normalized));
            links.Add(fullPath);
        }

        return links;
    }

    private static IEnumerable<KnowledgeChunk> SplitIntoChunks(string sourcePath, string markdown, List<string> links)
    {
        var sections = markdown.Split(["\r\n# ", "\n# "], StringSplitOptions.None);
        var chunks = new List<KnowledgeChunk>();
        var index = 0;

        foreach (var section in sections)
        {
            var normalized = section.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var title = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? sourcePath;
            foreach (var slice in Slice(normalized, 900))
            {
                chunks.Add(new KnowledgeChunk
                {
                    Id = CreateDeterministicGuid($"{sourcePath}:{index}:{title}"),
                    SourcePath = sourcePath,
                    Title = title.TrimStart('#', ' '),
                    Content = slice,
                    ChunkIndex = index++,
                    Links = links
                });
            }
        }

        return chunks;
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var bytes = hash[..16].ToArray();
        return new Guid(bytes);
    }

    private static IEnumerable<string> Slice(string text, int size)
    {
        for (var index = 0; index < text.Length; index += size)
        {
            var length = Math.Min(size, text.Length - index);
            yield return text.Substring(index, length);
        }
    }
}

