using PageApi.Shared.Core;
using PageApi.Shared.Kafka;

namespace CoreService.Services;

public sealed class CoreEventProcessor
{
    private readonly CoreStateRepository _repository;
    private readonly GeminiIntentAnalyzer _analyzer;
    private readonly CoreDecisionEngine _decisionEngine;
    private readonly KafkaCoreCommandPublisher _publisher;
    private readonly ILogger<CoreEventProcessor> _logger;

    public CoreEventProcessor(
        CoreStateRepository repository,
        GeminiIntentAnalyzer analyzer,
        CoreDecisionEngine decisionEngine,
        KafkaCoreCommandPublisher publisher,
        ILogger<CoreEventProcessor> logger)
    {
        _repository = repository;
        _analyzer = analyzer;
        _decisionEngine = decisionEngine;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task ProcessAsync(RawEventMessage rawEvent, CancellationToken cancellationToken)
    {
        rawEvent.EnsureIdentity();
        var inserted = await _repository.TryMarkReceivedAsync(rawEvent, cancellationToken);
        if (!inserted)
        {
            _logger.LogInformation("Skipping duplicate raw event EventId={EventId}", rawEvent.EventId);
            return;
        }

        var snapshot = await _repository.GetActorSnapshotAsync(rawEvent.UserId, cancellationToken);
        var ai = await _analyzer.AnalyzeAsync(rawEvent, cancellationToken);
        var decision = _decisionEngine.Decide(rawEvent, snapshot, ai);

        await _repository.ApplyDecisionAsync(rawEvent, decision, cancellationToken);

        if (decision.Action == CommandAction.None)
        {
            _logger.LogInformation("Core processed EventId={EventId} with no downstream command. Reason={Reason}", rawEvent.EventId, decision.Reason);
            return;
        }

        var command = new ReplyCommandMessage
        {
            CommandId = $"{rawEvent.EventId}:{decision.Action}",
            EventId = rawEvent.EventId,
            Action = decision.Action,
            Target = new CommandTarget
            {
                PageId = rawEvent.PageId,
                PostId = rawEvent.PostId,
                CommentId = rawEvent.CommentId,
                UserId = rawEvent.UserId
            },
            ReplyText = decision.ReplyText,
            Intent = decision.Intent,
            Sentiment = decision.Sentiment,
            RetryCount = 0
        };

        await _publisher.PublishAsync(command, cancellationToken);
    }
}
