using System.Text.Json.Serialization;

namespace PageApi.Shared.Kafka;

public sealed class RawEventMessage
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "facebook";

    [JsonPropertyName("page_id")]
    public string PageId { get; set; } = string.Empty;

    [JsonPropertyName("post_id")]
    public string? PostId { get; set; }

    [JsonPropertyName("comment_id")]
    public string? CommentId { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("received_at")]
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload_json")]
    public string PayloadJson { get; set; } = "{}";

    public void EnsureIdentity()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            Source = "facebook";
        }

        if (!string.IsNullOrWhiteSpace(CommentId))
        {
            EventType = string.IsNullOrWhiteSpace(EventType) ? "comment_created" : EventType;
            EventId = string.IsNullOrWhiteSpace(EventId) ? $"facebook:comment:{CommentId}" : EventId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(MessageId))
        {
            EventType = string.IsNullOrWhiteSpace(EventType) ? "message_created" : EventType;
            EventId = string.IsNullOrWhiteSpace(EventId) ? $"facebook:message:{MessageId}" : EventId;
            return;
        }

        EventType = string.IsNullOrWhiteSpace(EventType) ? "unknown" : EventType;
        EventId = string.IsNullOrWhiteSpace(EventId) ? $"facebook:unknown:{Guid.NewGuid():N}" : EventId;
    }
}

public sealed class ReplyCommandMessage
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = CommandAction.None;

    [JsonPropertyName("target")]
    public CommandTarget Target { get; set; } = new();

    [JsonPropertyName("reply_text")]
    public string? ReplyText { get; set; }

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "unknown";

    [JsonPropertyName("sentiment")]
    public string Sentiment { get; set; } = "neutral";

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CommandTarget
{
    [JsonPropertyName("page_id")]
    public string PageId { get; set; } = string.Empty;

    [JsonPropertyName("post_id")]
    public string? PostId { get; set; }

    [JsonPropertyName("comment_id")]
    public string? CommentId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
}

public sealed class FailedCommandMessage
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("last_error")]
    public string LastError { get; set; } = string.Empty;

    [JsonPropertyName("next_retry_at")]
    public DateTimeOffset? NextRetryAt { get; set; }

    [JsonPropertyName("payload")]
    public ReplyCommandMessage Payload { get; set; } = new();
}

public sealed class DeadLetterMessage
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("failed_at")]
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("final_error")]
    public string FinalError { get; set; } = string.Empty;

    [JsonPropertyName("original_topic")]
    public string OriginalTopic { get; set; } = KafkaTopics.SendFailed;

    [JsonPropertyName("payload")]
    public ReplyCommandMessage Payload { get; set; } = new();
}

public static class CommandAction
{
    public const string None = "none";
    public const string Reply = "reply";
    public const string Hide = "hide";
    public const string HideAndReview = "hide_and_review";
    public const string ManualReview = "manual_review";
}

public static class EventProcessingStatus
{
    public const string Received = "received";
    public const string Processed = "processed";
    public const string Replied = "replied";
    public const string PendingReview = "pending_review";
    public const string Failed = "failed";
}
