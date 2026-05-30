using System.Net;
using Microsoft.Extensions.Options;
using Page_API.Models;
using PageApi.Shared.Core;
using PageApi.Shared.Kafka;

namespace Page_API.Services;

public sealed class FacebookCommandHandler
{
    private readonly IFacebookService _facebookService;
    private readonly CommandStateRepository _repository;
    private readonly KafkaFailedCommandPublisher _failedPublisher;
    private readonly FacebookCircuitBreaker _circuitBreaker;
    private readonly ILogger<FacebookCommandHandler> _logger;

    public FacebookCommandHandler(
        IFacebookService facebookService,
        CommandStateRepository repository,
        KafkaFailedCommandPublisher failedPublisher,
        FacebookCircuitBreaker circuitBreaker,
        ILogger<FacebookCommandHandler> logger)
    {
        _facebookService = facebookService;
        _repository = repository;
        _failedPublisher = failedPublisher;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public async Task HandleAsync(ReplyCommandMessage command, CancellationToken cancellationToken)
    {
        if (await _repository.IsProcessedAsync(command, cancellationToken))
        {
            _logger.LogInformation("Skipping duplicate command CommandId={CommandId} Action={Action}", command.CommandId, command.Action);
            return;
        }

        if (command.Action == CommandAction.ManualReview)
        {
            await _repository.InsertManualReviewAsync(command, "core_manual_review", cancellationToken);
            await _repository.MarkProcessedAsync(command, EventProcessingStatus.PendingReview, cancellationToken);
            return;
        }

        if (!_circuitBreaker.CanExecute(DateTimeOffset.UtcNow))
        {
            await PublishFailedAsync(command, "facebook_circuit_breaker_open", cancellationToken);
            return;
        }

        try
        {
            if (command.Action == CommandAction.HideAndReview)
            {
                await _repository.InsertManualReviewAsync(command, "hide_and_review", cancellationToken);
                await HideAsync(command, cancellationToken);
            }
            else if (command.Action == CommandAction.Hide)
            {
                await HideAsync(command, cancellationToken);
            }
            else if (command.Action == CommandAction.Reply)
            {
                await ReplyAsync(command, cancellationToken);
            }

            _circuitBreaker.RecordSuccess();
            var status = command.Action == CommandAction.Reply
                ? EventProcessingStatus.Replied
                : EventProcessingStatus.Processed;
            await _repository.MarkProcessedAsync(command, status, cancellationToken);
        }
        catch (FacebookApiException ex)
        {
            if (IsTransient(ex.UpstreamStatusCode))
            {
                _circuitBreaker.RecordFailure(DateTimeOffset.UtcNow);
                await PublishFailedAsync(command, ex.Message, cancellationToken);
                return;
            }

            await _repository.InsertManualReviewAsync(command, $"facebook_non_retryable_error: {ex.Message}", cancellationToken);
            await _repository.MarkProcessedAsync(command, EventProcessingStatus.Failed, cancellationToken);
        }
    }

    private async Task ReplyAsync(ReplyCommandMessage command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target.CommentId) || string.IsNullOrWhiteSpace(command.ReplyText))
        {
            await _repository.InsertManualReviewAsync(command, "reply_missing_comment_or_text", cancellationToken);
            return;
        }

        await _facebookService.ReplyToCommentAsync(command.Target.CommentId, command.ReplyText);
    }

    private async Task HideAsync(ReplyCommandMessage command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target.CommentId))
        {
            await _repository.InsertManualReviewAsync(command, "hide_missing_comment_id", cancellationToken);
            return;
        }

        await _facebookService.HideCommentAsync(command.Target.CommentId);
    }

    private async Task PublishFailedAsync(ReplyCommandMessage command, string error, CancellationToken cancellationToken)
    {
        var failed = new FailedCommandMessage
        {
            CommandId = command.CommandId,
            EventId = command.EventId,
            RetryCount = command.RetryCount,
            LastError = error,
            Payload = command
        };

        await _repository.InsertFailedCommandAsync(failed, "failed", cancellationToken);
        await _failedPublisher.PublishAsync(failed, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == (HttpStatusCode)429
            || statusCode == HttpStatusCode.ServiceUnavailable
            || (int)statusCode >= 500;
    }
}
