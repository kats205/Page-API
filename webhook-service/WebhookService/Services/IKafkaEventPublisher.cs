using PageApi.Shared.Kafka;

namespace WebhookService.Services;

public interface IKafkaEventPublisher
{
    Task PublishAsync(IReadOnlyList<RawEventMessage> events, CancellationToken cancellationToken = default);
}
