using PageApi.Shared.Data;

namespace Page_API.Services;

public sealed class BackendDatabaseInitializer : IHostedService
{
    private readonly CommandStateRepository _repository;
    private readonly ILogger<BackendDatabaseInitializer> _logger;

    public BackendDatabaseInitializer(CommandStateRepository repository, ILogger<BackendDatabaseInitializer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(PageApiSchema.Sql, cancellationToken);
        _logger.LogInformation("Backend API database schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
