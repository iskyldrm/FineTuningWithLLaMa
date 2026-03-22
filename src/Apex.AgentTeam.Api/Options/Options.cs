namespace Apex.AgentTeam.Api.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string PostgresConnectionString { get; set; } = string.Empty;

    public string QdrantBaseUrl { get; set; } = "http://localhost:6333";

    public string QdrantCollectionName { get; set; } = "apex-knowledge";
}

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string DatabaseName { get; set; } = "apex_agent_team";
}

public sealed class ModelOptions
{
    public const string SectionName = "Model";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string ChatModel { get; set; } = "qwen2.5-coder:14b";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    public int PhysicalWorkerCount { get; set; } = 1;

    public double Temperature { get; set; } = 0.15;
}

public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    public string RootPath { get; set; } = "..\\..\\..\\..";

    public string RepositoriesRootPath { get; set; } = "..\\..\\..\\..\\..\\workspace-data\\repositories";

    public string KnowledgeEntryPoint { get; set; } = "APEX.md";

    public string ValidationCommand { get; set; } = "dotnet test Apex.AgentTeam.sln";

    public string RepositoryValidationCommand { get; set; } = "git diff --check";
}

public sealed class RuntimeOptions
{
    public const string SectionName = "Runtime";

    public string RegistryPath { get; set; } = "App_Data\\agent-runtime.json";

    public int DefaultMaxSteps { get; set; } = 8;

    public int MaxToolOutputCharacters { get; set; } = 6_000;
}

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string BaseUrl { get; set; } = "https://api.github.com";

    public string GraphQlUrl { get; set; } = "https://api.github.com/graphql";

    public string WebUrl { get; set; } = "https://github.com";

    public string RepositoryOwner { get; set; } = string.Empty;

    public string RepositoryName { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;
}
