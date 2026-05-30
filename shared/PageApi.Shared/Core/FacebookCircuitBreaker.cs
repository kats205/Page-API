namespace PageApi.Shared.Core;

public sealed class FacebookCircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 10;
    public int OpenSeconds { get; set; } = 30;
}

public sealed class FacebookCircuitBreaker
{
    private readonly FacebookCircuitBreakerOptions _options;
    private int _consecutiveFailures;
    private DateTimeOffset? _openedAt;

    public FacebookCircuitBreaker(FacebookCircuitBreakerOptions options)
    {
        _options = options;
    }

    public bool CanExecute(DateTimeOffset now)
    {
        if (_openedAt is null)
        {
            return true;
        }

        return now - _openedAt.Value >= TimeSpan.FromSeconds(_options.OpenSeconds);
    }

    public void RecordFailure(DateTimeOffset now)
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= _options.FailureThreshold)
        {
            _openedAt = now;
        }
    }

    public void RecordSuccess()
    {
        _consecutiveFailures = 0;
        _openedAt = null;
    }
}
