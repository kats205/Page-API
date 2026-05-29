namespace CoreService.Models;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string RawEventsTopic { get; set; } = "raw_events";
    public string ReplyCommandsTopic { get; set; } = "reply_commands";
    public string GroupId { get; set; } = "core-service";
    public string AutoOffsetReset { get; set; } = "Earliest";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=pageapi;Username=postgres;Password=postgres";
}

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-1.5-flash";
    public int TimeoutSeconds { get; set; } = 5;
}
