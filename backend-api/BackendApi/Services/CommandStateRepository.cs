using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Page_API.Models;
using PageApi.Shared.Kafka;

namespace Page_API.Services;

public sealed class CommandStateRepository
{
    private readonly PostgresOptions _options;

    public CommandStateRepository(IOptions<PostgresOptions> options)
    {
        _options = options.Value;
    }

    public async Task InitializeAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsProcessedAsync(ReplyCommandMessage command, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM processed_commands WHERE command_id = @command_id AND action = @action);";
        dbCommand.Parameters.AddWithValue("command_id", command.CommandId);
        dbCommand.Parameters.AddWithValue("action", command.Action);
        return (bool)(await dbCommand.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task MarkProcessedAsync(ReplyCommandMessage command, string status, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = """
            INSERT INTO processed_commands(command_id, action, event_id, status)
            VALUES (@command_id, @action, @event_id, @status)
            ON CONFLICT (command_id, action) DO NOTHING;

            UPDATE event_states
            SET status = @event_status, updated_at = NOW()
            WHERE event_id = @event_id;
            """;
        dbCommand.Parameters.AddWithValue("command_id", command.CommandId);
        dbCommand.Parameters.AddWithValue("action", command.Action);
        dbCommand.Parameters.AddWithValue("event_id", command.EventId);
        dbCommand.Parameters.AddWithValue("status", status);
        dbCommand.Parameters.AddWithValue("event_status", status);
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertManualReviewAsync(ReplyCommandMessage command, string reason, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = """
            INSERT INTO manual_review_items(event_id, actor_id, reason, payload_json)
            VALUES (@event_id, @actor_id, @reason, @payload_json);
            """;
        dbCommand.Parameters.AddWithValue("event_id", command.EventId);
        dbCommand.Parameters.AddWithValue("actor_id", (object?)command.Target.UserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("reason", reason);
        dbCommand.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(command));
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertFailedCommandAsync(FailedCommandMessage failed, string status, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = """
            INSERT INTO failed_commands(command_id, event_id, retry_count, last_error, payload_json, status, next_retry_at)
            VALUES (@command_id, @event_id, @retry_count, @last_error, @payload_json, @status, @next_retry_at);

            UPDATE event_states
            SET status = 'failed', reason = @last_error, updated_at = NOW()
            WHERE event_id = @event_id;
            """;
        dbCommand.Parameters.AddWithValue("command_id", failed.CommandId);
        dbCommand.Parameters.AddWithValue("event_id", failed.EventId);
        dbCommand.Parameters.AddWithValue("retry_count", failed.RetryCount);
        dbCommand.Parameters.AddWithValue("last_error", failed.LastError);
        dbCommand.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(failed.Payload));
        dbCommand.Parameters.AddWithValue("status", status);
        dbCommand.Parameters.AddWithValue("next_retry_at", (object?)failed.NextRetryAt?.UtcDateTime ?? DBNull.Value);
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
