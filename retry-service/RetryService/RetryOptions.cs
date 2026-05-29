namespace RetryService;

public sealed class KafkaRetryOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string SendFailedTopic { get; set; } = "send_failed";
    public string SendRetryTopic { get; set; } = "send_retry";
    public string DeadLetterTopic { get; set; } = "dead_letter";
    public string GroupId { get; set; } = "retry-service";
    public string AutoOffsetReset { get; set; } = "Earliest";
}
