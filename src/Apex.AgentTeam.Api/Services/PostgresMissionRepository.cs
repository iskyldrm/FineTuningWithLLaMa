using System.Text.Json;
using Apex.AgentTeam.Api.Hubs;
using Apex.AgentTeam.Api.Infrastructure;
using Apex.AgentTeam.Api.Models;
using Apex.AgentTeam.Api.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Apex.AgentTeam.Api.Services;

public sealed class PostgresMissionRepository : IMissionRepository
{
    private readonly StorageOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public PostgresMissionRepository(IOptions<StorageOptions> options)
    {
        _options = options.Value;
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

            await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                create table if not exists missions (
                    id uuid primary key,
                    updated_at timestamptz not null,
                    data jsonb not null
                );

                create table if not exists activities (
                    id uuid primary key,
                    mission_id uuid not null,
                    created_at timestamptz not null,
                    data jsonb not null
                );

                create index if not exists idx_missions_updated_at on missions(updated_at desc);
                create index if not exists idx_activities_mission_id on activities(mission_id, created_at desc);
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveMissionAsync(Mission mission, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(mission, JsonDefaults.Web);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            insert into missions(id, updated_at, data)
            values (@id, @updated_at, cast(@data as jsonb))
            on conflict (id) do update
            set updated_at = excluded.updated_at,
                data = excluded.data;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", mission.Id);
        command.Parameters.AddWithValue("updated_at", mission.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue("data", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Mission?> GetMissionAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "select data::text from missions where id = @id limit 1;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", missionId);

        var raw = await command.ExecuteScalarAsync(cancellationToken) as string;
        return raw is null ? null : JsonSerializer.Deserialize<Mission>(raw, JsonDefaults.Web);
    }

    public async Task<Mission?> GetLatestMissionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "select data::text from missions order by updated_at desc limit 1;";
        await using var command = new NpgsqlCommand(sql, connection);
        var raw = await command.ExecuteScalarAsync(cancellationToken) as string;
        return raw is null ? null : JsonSerializer.Deserialize<Mission>(raw, JsonDefaults.Web);
    }

    public async Task<IReadOnlyList<ActivityEvent>> GetActivitiesAsync(Guid missionId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "select data::text from activities where mission_id = @mission_id order by created_at desc limit 80;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", missionId);

        var list = new List<ActivityEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var raw = reader.GetString(0);
            var item = JsonSerializer.Deserialize<ActivityEvent>(raw, JsonDefaults.Web);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task AppendActivityAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(activityEvent, JsonDefaults.Web);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "insert into activities(id, mission_id, created_at, data) values (@id, @mission_id, @created_at, cast(@data as jsonb));";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", activityEvent.Id);
        command.Parameters.AddWithValue("mission_id", activityEvent.MissionId);
        command.Parameters.AddWithValue("created_at", activityEvent.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("data", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(Mission Mission, PatchProposal Proposal)?> FindPatchProposalAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "select data::text from missions order by updated_at desc;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var raw = reader.GetString(0);
            var mission = JsonSerializer.Deserialize<Mission>(raw, JsonDefaults.Web);
            var proposal = mission?.PatchProposals.FirstOrDefault(item => item.Id == proposalId);
            if (mission is not null && proposal is not null)
            {
                return (mission, proposal);
            }
        }

        return null;
    }
}

public sealed class SignalRRealtimeStream : IActivityStream, IProgressStream
{
    private readonly IHubContext<ActivityHub> _hubContext;

    public SignalRRealtimeStream(IHubContext<ActivityHub> hubContext)
    {
        _hubContext = hubContext;
    }

    Task IActivityStream.PublishAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
    {
        return _hubContext.Clients.All.SendAsync("activity", activityEvent, cancellationToken);
    }

    Task IProgressStream.PublishAsync(ProgressLog progressLog, CancellationToken cancellationToken)
    {
        return _hubContext.Clients.All.SendAsync("progress", progressLog, cancellationToken);
    }
}
