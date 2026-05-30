using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Page_API.Models;
using PageApi.Shared.Kafka;

namespace Page_API.Services;

public sealed class FacebookCommandConsumerService : BackgroundService
{
    private readonly CommandConsumerOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacebookCommandConsumerService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FacebookCommandConsumerService(
        IOptions<CommandConsumerOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<FacebookCommandConsumerService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Facebook command consumer is disabled by configuration.");
            return Task.CompletedTask;
        }

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
        consumer.Subscribe(new[] { _options.ReplyCommandsTopic, _options.SendRetryTopic });
        _logger.LogInformation("Backend command consumer started Topics={Topics}", string.Join(",", new[] { _options.ReplyCommandsTopic, _options.SendRetryTopic }));

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
                    _logger.LogError(ex, "Backend command consume error.");
                    continue;
                }

                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                if (!TryBuildCommand(consumeResult.Topic, consumeResult.Message.Value, out var command))
                {
                    _logger.LogWarning("Skipping invalid command payload at {Offset}", consumeResult.TopicPartitionOffset);
                    consumer.Commit(consumeResult);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<FacebookCommandHandler>();
                    handler.HandleAsync(command!, stoppingToken).GetAwaiter().GetResult();
                    consumer.Commit(consumeResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backend failed to process CommandId={CommandId}; offset not committed.", command?.CommandId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Backend command consumer stopping.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private bool TryBuildCommand(string topic, string value, out ReplyCommandMessage? command)
    {
        command = null;
        try
        {
            if (topic == _options.SendRetryTopic)
            {
                var failed = JsonSerializer.Deserialize<FailedCommandMessage>(value, JsonOptions);
                command = failed?.Payload;
                if (command is not null && failed is not null)
                {
                    command.RetryCount = failed.RetryCount;
                }
            }
            else
            {
                command = JsonSerializer.Deserialize<ReplyCommandMessage>(value, JsonOptions);
            }

            return command is not null
                && !string.IsNullOrWhiteSpace(command.CommandId)
                && !string.IsNullOrWhiteSpace(command.Action);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to deserialize command payload.");
            return false;
        }
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value)
    {
        return Enum.TryParse<AutoOffsetReset>(value, true, out var parsed)
            ? parsed
            : AutoOffsetReset.Earliest;
    }
}
