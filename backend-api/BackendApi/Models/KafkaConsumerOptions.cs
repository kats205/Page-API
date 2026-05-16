namespace Page_API.Models
{
    public class KafkaConsumerOptions
    {
        public string BootstrapServers { get; set; } = string.Empty;
        public string Topic { get; set; } = "facebook.events.normalized";
        public string GroupId { get; set; } = "core-service-facebook-events";
        public string AutoOffsetReset { get; set; } = "Earliest";
    }
}
