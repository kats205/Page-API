namespace Page_API.Models;

public sealed class CommandConsumerOptions
{
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ReplyCommandsTopic { get; set; } = "reply_commands";
    public string SendRetryTopic { get; set; } = "send_retry";
    public string SendFailedTopic { get; set; } = "send_failed";
    public string GroupId { get; set; } = "backend-api-commands";
    public string AutoOffsetReset { get; set; } = "Earliest";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=pageapi;Username=postgres;Password=postgres";
}
