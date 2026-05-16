using System.Text.Json.Serialization;

namespace BackendApi.Models;

public class ReplyCommand
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = "reply";

    [JsonPropertyName("target")]
    public ReplyTarget Target { get; set; } = new();

    [JsonPropertyName("reply_text")]
    public string ReplyText { get; set; } = string.Empty;

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("sentiment")]
    public string? Sentiment { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReplyTarget
{
    [JsonPropertyName("page_id")]
    public string PageId { get; set; } = string.Empty;

    [JsonPropertyName("comment_id")]
    public string CommentId { get; set; } = string.Empty;
}

public class RetryMessage
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
    public string? LastError { get; set; }

    [JsonPropertyName("next_retry_at")]
    public DateTimeOffset? NextRetryAt { get; set; }

    [JsonPropertyName("payload")]
    public ReplyCommand? Payload { get; set; }
}
