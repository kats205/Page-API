using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Page_API.Models;
using PageApi.Shared.Kafka;

namespace Page_API.Services;

public sealed class KafkaFailedCommandPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly CommandConsumerOptions _options;
    private readonly ILogger<KafkaFailedCommandPublisher> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public KafkaFailedCommandPublisher(IOptions<CommandConsumerOptions> options, ILogger<KafkaFailedCommandPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = "backend-api",
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
    }

    public async Task PublishAsync(FailedCommandMessage failed, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(failed, JsonOptions);
        var delivery = await _producer.ProduceAsync(
            _options.SendFailedTopic,
            new Message<string, string>
            {
                Key = failed.CommandId,
                Value = value
            },
            cancellationToken);

        _logger.LogInformation(
            "Published failed command CommandId={CommandId} RetryCount={RetryCount} Topic={Topic} Offset={Offset}",
            failed.CommandId,
            failed.RetryCount,
            delivery.Topic,
            delivery.Offset.Value);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
