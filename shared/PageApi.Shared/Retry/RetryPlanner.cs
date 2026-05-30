namespace PageApi.Shared.Retry;

public sealed class RetryOptions
{
    public int MaxAttempts { get; set; } = 3;
}

public sealed record RetryPlan(bool SendToDeadLetter, TimeSpan Delay, int NextRetryCount);

public sealed class RetryPlanner
{
    private readonly RetryOptions _options;

    public RetryPlanner(RetryOptions options)
    {
        _options = options;
    }

    public RetryPlan Plan(int retryCount)
    {
        if (retryCount >= _options.MaxAttempts)
        {
            return new RetryPlan(true, TimeSpan.Zero, retryCount);
        }

        var seconds = Math.Pow(2, retryCount);
        return new RetryPlan(false, TimeSpan.FromSeconds(seconds), retryCount + 1);
    }
}
