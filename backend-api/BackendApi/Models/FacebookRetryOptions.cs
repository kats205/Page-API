namespace Page_API.Models;

public sealed class FacebookRetryOptions
{
    public int MaxRetries { get; set; } = 2;
    public int BaseDelayMilliseconds { get; set; } = 500;
}
