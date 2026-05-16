using Page_API.Models;

namespace Page_API.Services
{
    public class FacebookEventHandler : IFacebookEventHandler
    {
        private readonly ILogger<FacebookEventHandler> _logger;

        public FacebookEventHandler(ILogger<FacebookEventHandler> logger)
        {
            _logger = logger;
        }

        public Task HandleAsync(NormalizedFacebookEvent normalizedEvent, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Processed facebook event EventId={EventId} EventType={EventType} PageId={PageId} PostId={PostId} CommentId={CommentId}",
                normalizedEvent.EventId,
                normalizedEvent.EventType,
                normalizedEvent.PageId,
                normalizedEvent.PostId,
                normalizedEvent.CommentId);

            return Task.CompletedTask;
        }
    }
}
