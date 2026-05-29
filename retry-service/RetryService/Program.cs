using PageApi.Shared.Retry;
using RetryService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KafkaRetryOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<RetryOptions>(builder.Configuration.GetSection("Retry"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetryOptions>>().Value;
    return new RetryPlanner(options);
});
builder.Services.AddHostedService<RetryWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "retry-service" }));

app.Run();
