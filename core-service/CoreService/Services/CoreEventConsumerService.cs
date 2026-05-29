using System.Text.Json;
using Confluent.Kafka;
using CoreService.Models;
using Microsoft.Extensions.Options;
using PageApi.Shared.Kafka;

namespace CoreService.Services;

public sealed class CoreEventConsumerService : BackgroundService
{
    private readonly KafkaOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoreEventConsumerService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CoreEventConsumerService(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<CoreEventConsumerService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = ParseAutoOffsetReset(_options.AutoOffsetReset)
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.RawEventsTopic);
        _logger.LogInformation("Core consumer started Topic={Topic} GroupId={GroupId}", _options.RawEventsTopic, _options.GroupId);

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
                    _logger.LogError(ex, "Core Kafka consume error.");
                    continue;
                }

                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                RawEventMessage? rawEvent;
                try
                {
                    rawEvent = JsonSerializer.Deserialize<RawEventMessage>(consumeResult.Message.Value, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid raw event payload at {Offset}; committing poison message.", consumeResult.TopicPartitionOffset);
                    consumer.Commit(consumeResult);
                    continue;
                }

                if (rawEvent is null || string.IsNullOrWhiteSpace(rawEvent.EventId))
                {
                    _logger.LogWarning("Raw event missing EventId at {Offset}; committing poison message.", consumeResult.TopicPartitionOffset);
                    consumer.Commit(consumeResult);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<CoreEventProcessor>();
                    processor.ProcessAsync(rawEvent, stoppingToken).GetAwaiter().GetResult();
                    consumer.Commit(consumeResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Core failed to process EventId={EventId}; offset not committed.", rawEvent.EventId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Core consumer stopping.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value)
    {
        return Enum.TryParse<AutoOffsetReset>(value, true, out var parsed)
            ? parsed
            : AutoOffsetReset.Earliest;
    }
}
