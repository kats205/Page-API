using System.Text.Json;
using CoreService.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using PageApi.Shared.Core;
using PageApi.Shared.Data;
using PageApi.Shared.Kafka;

namespace CoreService.Services;

public sealed class CoreStateRepository
{
    private readonly PostgresOptions _options;

    public CoreStateRepository(IOptions<PostgresOptions> options)
    {
        _options = options.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PageApiSchema.Sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryMarkReceivedAsync(RawEventMessage rawEvent, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO event_states (
                event_id, event_type, source, page_id, post_id, comment_id, user_id,
                message, status, created_at, received_at, updated_at
            )
            VALUES (
                @event_id, @event_type, @source, @page_id, @post_id, @comment_id, @user_id,
                @message, @status, @created_at, @received_at, NOW()
            )
            ON CONFLICT (event_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("event_id", rawEvent.EventId);
        command.Parameters.AddWithValue("event_type", rawEvent.EventType);
        command.Parameters.AddWithValue("source", rawEvent.Source);
        command.Parameters.AddWithValue("page_id", rawEvent.PageId);
        command.Parameters.AddWithValue("post_id", (object?)rawEvent.PostId ?? DBNull.Value);
        command.Parameters.AddWithValue("comment_id", (object?)rawEvent.CommentId ?? DBNull.Value);
        command.Parameters.AddWithValue("user_id", (object?)rawEvent.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("message", (object?)rawEvent.Message ?? DBNull.Value);
        command.Parameters.AddWithValue("status", EventProcessingStatus.Received);
        command.Parameters.AddWithValue("created_at", rawEvent.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("received_at", rawEvent.ReceivedAt.UtcDateTime);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<ActorProcessingSnapshot> GetActorSnapshotAsync(string? actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return ActorProcessingSnapshot.Empty;
        }

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM actor_activity WHERE actor_id = @actor_id AND created_at >= NOW() - INTERVAL '1 minute') AS events_last_minute,
                (SELECT COUNT(*) FROM actor_spam_events WHERE actor_id = @actor_id AND created_at >= NOW() - INTERVAL '24 hours') AS spam_last_day,
                EXISTS(SELECT 1 FROM blacklisted_actors WHERE actor_id = @actor_id) AS is_blacklisted;
            """;
        command.Parameters.AddWithValue("actor_id", actorId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ActorProcessingSnapshot.Empty;
        }

        return new ActorProcessingSnapshot(
            EventsInLastMinute: reader.GetInt64(0) > int.MaxValue ? int.MaxValue : (int)reader.GetInt64(0),
            SpamEventsInLast24Hours: reader.GetInt64(1) > int.MaxValue ? int.MaxValue : (int)reader.GetInt64(1),
            IsBlacklisted: reader.GetBoolean(2));
    }

    public async Task ApplyDecisionAsync(RawEventMessage rawEvent, CoreDecision decision, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, """
            INSERT INTO actor_activity(actor_id, event_id)
            VALUES (@actor_id, @event_id)
            ON CONFLICT DO NOTHING;
            """, cancellationToken,
            ("actor_id", rawEvent.UserId ?? "unknown"),
            ("event_id", rawEvent.EventId));

        await ExecuteAsync(connection, transaction, """
            UPDATE event_states
            SET intent = @intent,
                sentiment = @sentiment,
                status = @status,
                reason = @reason,
                updated_at = NOW()
            WHERE event_id = @event_id;
            """, cancellationToken,
            ("intent", decision.Intent),
            ("sentiment", decision.Sentiment),
            ("status", decision.Status),
            ("reason", decision.Reason),
            ("event_id", rawEvent.EventId));

        if (decision.IsSpam)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO actor_spam_events(actor_id, event_id, reason)
                VALUES (@actor_id, @event_id, @reason);
                """, cancellationToken,
                ("actor_id", rawEvent.UserId ?? "unknown"),
                ("event_id", rawEvent.EventId),
                ("reason", decision.Reason));
        }

        if (decision.ShouldBlacklist && !string.IsNullOrWhiteSpace(rawEvent.UserId))
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO blacklisted_actors(actor_id, reason)
                VALUES (@actor_id, @reason)
                ON CONFLICT (actor_id) DO NOTHING;
                """, cancellationToken,
                ("actor_id", rawEvent.UserId),
                ("reason", decision.Reason));
        }

        if (decision.Action is CommandAction.ManualReview or CommandAction.HideAndReview)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO manual_review_items(event_id, actor_id, reason, payload_json)
                VALUES (@event_id, @actor_id, @reason, @payload_json);
                """, cancellationToken,
                ("event_id", rawEvent.EventId),
                ("actor_id", (object?)rawEvent.UserId ?? DBNull.Value),
                ("reason", decision.Reason),
                ("payload_json", JsonSerializer.Serialize(rawEvent)));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
