using System.Text.Json;
using Confluent.Kafka;
using CoreService.Models;
using Microsoft.Extensions.Options;
using PageApi.Shared.Kafka;

namespace CoreService.Services;

public sealed class KafkaCoreCommandPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaCoreCommandPublisher> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public KafkaCoreCommandPublisher(IOptions<KafkaOptions> options, ILogger<KafkaCoreCommandPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = "core-service",
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
    }

    public async Task PublishAsync(ReplyCommandMessage command, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(command, JsonOptions);
        var delivery = await _producer.ProduceAsync(
            _options.ReplyCommandsTopic,
            new Message<string, string>
            {
                Key = command.CommandId,
                Value = value
            },
            cancellationToken);

        _logger.LogInformation(
            "Published reply command CommandId={CommandId} Action={Action} Topic={Topic} Offset={Offset}",
            command.CommandId,
            command.Action,
            delivery.Topic,
            delivery.Offset.Value);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
