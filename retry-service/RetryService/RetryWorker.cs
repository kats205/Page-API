using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using PageApi.Shared.Kafka;
using PageApi.Shared.Retry;

namespace RetryService;

public sealed class RetryWorker : BackgroundService
{
    private readonly KafkaRetryOptions _kafkaOptions;
    private readonly RetryPlanner _retryPlanner;
    private readonly ILogger<RetryWorker> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RetryWorker(
        IOptions<KafkaRetryOptions> kafkaOptions,
        RetryPlanner retryPlanner,
        ILogger<RetryWorker> logger)
    {
        _kafkaOptions = kafkaOptions.Value;
        _retryPlanner = retryPlanner;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = ParseAutoOffsetReset(_kafkaOptions.AutoOffsetReset)
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            ClientId = "retry-service",
            EnableIdempotence = true,
            Acks = Acks.All
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        consumer.Subscribe(_kafkaOptions.SendFailedTopic);
        _logger.LogInformation("Retry service consuming Topic={Topic}", _kafkaOptions.SendFailedTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumeResult;
                try
                {
                    consumeResult = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Retry consume error.");
                    continue;
                }

                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                FailedCommandMessage? failed;
                try
                {
                    failed = JsonSerializer.Deserialize<FailedCommandMessage>(consumeResult.Message.Value, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid failed command payload; committing poison message.");
                    consumer.Commit(consumeResult);
                    continue;
                }

                if (failed is null || string.IsNullOrWhiteSpace(failed.CommandId))
                {
                    _logger.LogWarning("Failed command missing CommandId; committing poison message.");
                    consumer.Commit(consumeResult);
                    continue;
                }

                try
                {
                    HandleFailedCommandAsync(failed, producer, stoppingToken).GetAwaiter().GetResult();
                    consumer.Commit(consumeResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry service failed to process CommandId={CommandId}; offset not committed.", failed.CommandId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Retry service stopping.");
        }
        finally
        {
            producer.Flush(TimeSpan.FromSeconds(5));
            consumer.Close();
        }
    }

    private async Task HandleFailedCommandAsync(
        FailedCommandMessage failed,
        IProducer<string, string> producer,
        CancellationToken cancellationToken)
    {
        var plan = _retryPlanner.Plan(failed.RetryCount);
        if (plan.SendToDeadLetter)
        {
            var deadLetter = new DeadLetterMessage
            {
                CommandId = failed.CommandId,
                EventId = failed.EventId,
                RetryCount = failed.RetryCount,
                FinalError = failed.LastError,
                OriginalTopic = _kafkaOptions.SendFailedTopic,
                Payload = failed.Payload
            };

            await producer.ProduceAsync(
                _kafkaOptions.DeadLetterTopic,
                new Message<string, string>
                {
                    Key = deadLetter.CommandId,
                    Value = JsonSerializer.Serialize(deadLetter, JsonOptions)
                },
                cancellationToken);
            _logger.LogWarning("Published dead letter CommandId={CommandId}", failed.CommandId);
            return;
        }

        await Task.Delay(plan.Delay, cancellationToken);
        failed.RetryCount = plan.NextRetryCount;
        failed.NextRetryAt = DateTimeOffset.UtcNow;
        failed.Payload.RetryCount = plan.NextRetryCount;

        await producer.ProduceAsync(
            _kafkaOptions.SendRetryTopic,
            new Message<string, string>
            {
                Key = failed.CommandId,
                Value = JsonSerializer.Serialize(failed, JsonOptions)
            },
            cancellationToken);
        _logger.LogInformation("Published retry CommandId={CommandId} RetryCount={RetryCount}", failed.CommandId, failed.RetryCount);
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value)
    {
        return Enum.TryParse<AutoOffsetReset>(value, true, out var parsed)
            ? parsed
            : AutoOffsetReset.Earliest;
    }
}
