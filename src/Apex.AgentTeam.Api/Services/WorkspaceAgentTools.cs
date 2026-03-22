using System.Text;
using Apex.AgentTeam.Api.Models;

namespace Apex.AgentTeam.Api.Services;

public sealed partial class GitWorkspaceToolset
{
    private static readonly string[] SearchableExtensions = [".cs", ".csproj", ".sln", ".json", ".md", ".ts", ".tsx", ".js", ".jsx", ".css", ".scss", ".html", ".yml", ".yaml", ".txt", ".py"];

    public async Task<string> ListFilesAsync(Mission mission, string? pattern, int limit, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var normalizedPattern = (pattern ?? string.Empty).Trim().Replace('\\', '/');
        var files = EnumerateWorkspaceFiles(root)
            .Where(path => string.IsNullOrWhiteSpace(normalizedPattern) || path.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 400))
            .ToList();

        return files.Count == 0 ? "No files matched the requested pattern." : string.Join(Environment.NewLine, files);
    }

    public async Task<string> ReadFileAsync(Mission mission, string relativePath, int startLine, int maxLines, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var fullPath = ResolvePathWithinRoot(root, relativePath);
        if (!File.Exists(fullPath))
        {
            return $"File not found: {relativePath}";
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        var safeStartLine = Math.Max(1, startLine);
        var safeMaxLines = Math.Clamp(maxLines, 1, 400);
        var slice = lines
            .Skip(safeStartLine - 1)
            .Take(safeMaxLines)
            .Select((line, index) => $"{safeStartLine + index,4}: {line}")
            .ToList();

        return slice.Count == 0
            ? $"File is empty or there are no lines after {safeStartLine}: {relativePath}"
            : string.Join(Environment.NewLine, slice);
    }

    public async Task<string> WriteFileAsync(Mission mission, string relativePath, string content, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var fullPath = ResolvePathWithinRoot(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var existing = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : null;
        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            return $"No changes written to {relativePath}.";
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return $"Wrote {relativePath} ({content.Length} chars).";
    }

    public async Task<string> SearchCodeAsync(Mission mission, string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Search query is required.";
        }

        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var matches = new List<string>();
        foreach (var relativePath in EnumerateWorkspaceFiles(root).Where(path => IsSearchableFile(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
            }
            catch
            {
                continue;
            }

            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add($"{relativePath}:{index + 1}: {lines[index].Trim()}");
                if (matches.Count >= Math.Clamp(limit, 1, 120))
                {
                    return string.Join(Environment.NewLine, matches);
                }
            }
        }

        return matches.Count == 0 ? $"No matches found for '{query}'." : string.Join(Environment.NewLine, matches);
    }

    public async Task<string> RunTerminalCommandAsync(Mission mission, string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Blocked: terminal command is empty.";
        }

        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var result = await RunShellAsync(command, root, cancellationToken);
        return FormatProcessResult(result);
    }

    public async Task<string> GetGitStatusAsync(Mission mission, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var result = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" status --short --branch", root, cancellationToken);
        return FormatProcessResult(result);
    }

    public async Task<string> GetGitDiffAsync(Mission mission, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var statusResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" status --porcelain", root, cancellationToken);
        var entries = ParseStatusEntries(statusResult.StdOut);
        if (entries.Count == 0)
        {
            return "Working tree clean.";
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessResult diffResult;
            if (entry.IsUntracked)
            {
                diffResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" diff --binary --no-index -- /dev/null \"{entry.Path}\"", root, cancellationToken);
            }
            else
            {
                diffResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" diff --binary -- \"{entry.Path}\"", root, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(diffResult.StdOut))
            {
                builder.AppendLine(diffResult.StdOut.TrimEnd());
            }
        }

        return builder.Length == 0 ? "Working tree clean." : builder.ToString().Trim();
    }

    public async Task<string> CommitAsync(Mission mission, string message, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        await RunProcessAsync("git", $"config user.name \"Apex Agent Team\"", root, cancellationToken);
        await RunProcessAsync("git", $"config user.email \"apex-agent-team@local\"", root, cancellationToken);

        var status = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" status --porcelain", root, cancellationToken);
        if (string.IsNullOrWhiteSpace(status.StdOut))
        {
            return "No changes to commit.";
        }

        var addResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" add -A", root, cancellationToken);
        if (addResult.ExitCode != 0)
        {
            return FormatProcessResult(addResult);
        }

        var commitResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" commit -m \"{message.Replace("\"", "\\\"", StringComparison.Ordinal)}\"", root, cancellationToken);
        return FormatProcessResult(commitResult);
    }

    public async Task<string> PushAsync(Mission mission, string? branchName, CancellationToken cancellationToken)
    {
        var root = await EnsureWorkspaceRootAsync(mission, cancellationToken);
        mission.WorkspaceRootPath = root;
        var resolvedBranch = branchName;
        if (string.IsNullOrWhiteSpace(resolvedBranch))
        {
            var branchResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" rev-parse --abbrev-ref HEAD", root, cancellationToken);
            resolvedBranch = branchResult.StdOut.Trim();
        }

        if (string.IsNullOrWhiteSpace(resolvedBranch))
        {
            return "Cannot determine the current branch.";
        }

        var pushResult = await RunProcessAsync("git", $"-c safe.directory=\"{root}\" push -u origin \"{resolvedBranch}\"", root, cancellationToken);
        return FormatProcessResult(pushResult);
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IgnoredSegments.Any(segment => path.Contains($"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
    }

    private static bool IsSearchableFile(string path)
    {
        return SearchableExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePathWithinRoot(string root, string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && !string.Equals(combined.TrimEnd(Path.DirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{relativePath}' escapes the workspace root.");
        }

        return combined;
    }

    private static string FormatProcessResult(ProcessResult result)
    {
        var buffer = new StringBuilder();
        buffer.AppendLine($"ExitCode: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            buffer.AppendLine(result.StdOut.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            buffer.AppendLine(result.StdErr.Trim());
        }

        return buffer.ToString().Trim();
    }

    private static List<GitStatusEntry> ParseStatusEntries(string status)
    {
        return status.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Select(line =>
            {
                var isUntracked = line.StartsWith("?? ", StringComparison.Ordinal);
                var path = line.Length > 3 ? line[3..].Trim() : string.Empty;
                if (path.Contains("->", StringComparison.Ordinal))
                {
                    path = path.Split("->", StringSplitOptions.TrimEntries).Last();
                }

                return new GitStatusEntry(path, isUntracked);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record GitStatusEntry(string Path, bool IsUntracked);
}
