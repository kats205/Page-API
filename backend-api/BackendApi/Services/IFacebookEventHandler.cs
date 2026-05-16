using Page_API.Models;

namespace Page_API.Services
{
    public interface IFacebookEventHandler
    {
        Task HandleAsync(NormalizedFacebookEvent normalizedEvent, CancellationToken cancellationToken);
    }
}
