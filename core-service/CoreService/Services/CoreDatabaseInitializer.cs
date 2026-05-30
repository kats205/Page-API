namespace CoreService.Services;

public sealed class CoreDatabaseInitializer : IHostedService
{
    private readonly CoreStateRepository _repository;
    private readonly ILogger<CoreDatabaseInitializer> _logger;

    public CoreDatabaseInitializer(CoreStateRepository repository, ILogger<CoreDatabaseInitializer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);
        _logger.LogInformation("Core Service database schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
