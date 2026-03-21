using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Apex.AgentTeam.Api.Services;

public sealed class MongoTelemetryStore : IProgressLogStore, IChatStore
{
    private static int _guidSerializerConfigured;
    private readonly MongoOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private IMongoCollection<ProgressLog>? _progressLogs;
    private IMongoCollection<ChatThread>? _threads;
    private IMongoCollection<ChatMessage>? _messages;

    public MongoTelemetryStore(IOptions<MongoOptions> options)
    {
        _options = options.Value;
        ConfigureGuidSerialization();
    }

    private static void ConfigureGuidSerialization()
    {
        if (Interlocked.Exchange(ref _guidSerializerConfigured, 1) != 0)
        {
            return;
        }

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var database = new MongoClient(_options.ConnectionString).GetDatabase(_options.DatabaseName);
            _progressLogs = database.GetCollection<ProgressLog>("progress_logs");
            _threads = database.GetCollection<ChatThread>("chat_threads");
            _messages = database.GetCollection<ChatMessage>("chat_messages");

            await _progressLogs.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProgressLog>(Builders<ProgressLog>.IndexKeys.Descending(item => item.CreatedAt)),
                new CreateIndexModel<ProgressLog>(Builders<ProgressLog>.IndexKeys.Ascending(item => item.MissionId).Descending(item => item.CreatedAt))
            ], cancellationToken: cancellationToken);

            await _threads.Indexes.CreateOneAsync(
                new CreateIndexModel<ChatThread>(Builders<ChatThread>.IndexKeys.Descending(item => item.UpdatedAt)),
                cancellationToken: cancellationToken);

            await _messages.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ChatMessage>(Builders<ChatMessage>.IndexKeys.Ascending(item => item.ThreadId).Ascending(item => item.CreatedAt)),
                new CreateIndexModel<ChatMessage>(Builders<ChatMessage>.IndexKeys.Descending(item => item.CreatedAt))
            ], cancellationToken: cancellationToken);

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(ProgressLog progressLog, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await ProgressLogs.InsertOneAsync(progressLog, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ProgressLog>> GetByMissionAsync(Guid missionId, int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var items = await ProgressLogs.Find(item => item.MissionId == missionId)
            .SortByDescending(item => item.CreatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        items.Reverse();
        return items;
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await Threads.Find(FilterDefinition<ChatThread>.Empty)
            .SortByDescending(item => item.UpdatedAt)
            .Limit(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatThread?> GetThreadAsync(Guid threadId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await Threads.Find(item => item.Id == threadId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ChatThread> CreateThreadAsync(ChatThread thread, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await Threads.InsertOneAsync(thread, cancellationToken: cancellationToken);
        return thread;
    }

    public async Task UpdateThreadAsync(ChatThread thread, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await Threads.ReplaceOneAsync(item => item.Id == thread.Id, thread, new ReplaceOptions { IsUpsert = true }, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await Messages.Find(item => item.ThreadId == threadId)
            .SortBy(item => item.CreatedAt)
            .Limit(400)
            .ToListAsync(cancellationToken);
    }

    public async Task AppendMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await Messages.InsertOneAsync(message, cancellationToken: cancellationToken);
    }

    private IMongoCollection<ProgressLog> ProgressLogs => _progressLogs ?? throw new InvalidOperationException("Mongo progress collection is not initialized.");

    private IMongoCollection<ChatThread> Threads => _threads ?? throw new InvalidOperationException("Mongo thread collection is not initialized.");

    private IMongoCollection<ChatMessage> Messages => _messages ?? throw new InvalidOperationException("Mongo message collection is not initialized.");
}

