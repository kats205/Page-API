using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Page_API.Models;

namespace Page_API.Services
{
    public class FacebookEventConsumerService : BackgroundService
    {
        private readonly KafkaConsumerOptions _options;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FacebookEventConsumerService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookEventConsumerService(
            IOptions<KafkaConsumerOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FacebookEventConsumerService> logger)
        {
            _options = options.Value;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_options.BootstrapServers)
                || string.IsNullOrWhiteSpace(_options.Topic)
                || string.IsNullOrWhiteSpace(_options.GroupId))
            {
                _logger.LogWarning("Kafka consumer is disabled because KafkaConsumer configuration is incomplete.");
                return Task.CompletedTask;
            }

            return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
        }

        private void ConsumeLoop(CancellationToken stoppingToken)
        {
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _options.GroupId,
                EnableAutoCommit = false,
                AutoOffsetReset = ParseAutoOffsetReset(_options.AutoOffsetReset)
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(_options.Topic);

            _logger.LogInformation(
                "Kafka consumer started. Topic={Topic} GroupId={GroupId} BootstrapServers={BootstrapServers}",
                _options.Topic,
                _options.GroupId,
                _options.BootstrapServers);

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
                        _logger.LogError(ex, "Kafka consume error.");
                        continue;
                    }

                    if (consumeResult?.Message?.Value is null)
                    {
                        continue;
                    }

                    if (!TryDeserialize(consumeResult.Message.Value, out var normalizedEvent))
                    {
                        _logger.LogWarning(
                            "Skipping invalid event at {TopicPartitionOffset}.",
                            consumeResult.TopicPartitionOffset);
                        consumer.Commit(consumeResult);
                        continue;
                    }

                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var handler = scope.ServiceProvider.GetRequiredService<IFacebookEventHandler>();
                        handler.HandleAsync(normalizedEvent!, stoppingToken).GetAwaiter().GetResult();

                        consumer.Commit(consumeResult);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to process event at {TopicPartitionOffset}. EventId={EventId}",
                            consumeResult.TopicPartitionOffset,
                            normalizedEvent?.EventId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka consumer stopping.");
            }
            finally
            {
                consumer.Close();
            }
        }

        private bool TryDeserialize(string payload, out NormalizedFacebookEvent? normalizedEvent)
        {
            normalizedEvent = null;
            try
            {
                normalizedEvent = JsonSerializer.Deserialize<NormalizedFacebookEvent>(payload, _jsonOptions);
                if (normalizedEvent is null || string.IsNullOrWhiteSpace(normalizedEvent.EventId))
                {
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Unable to deserialize normalized facebook event payload.");
                return false;
            }
        }

        private static AutoOffsetReset ParseAutoOffsetReset(string value)
        {
            if (Enum.TryParse<AutoOffsetReset>(value, true, out var parsed))
            {
                return parsed;
            }

            return AutoOffsetReset.Earliest;
        }
    }
}
