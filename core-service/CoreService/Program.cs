using CoreService.Models;
using CoreService.Services;
using PageApi.Shared.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<CoreProcessingOptions>(builder.Configuration.GetSection("Processing"));

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CoreProcessingOptions>>().Value;
    return new CoreDecisionEngine(options);
});
builder.Services.AddSingleton<CoreStateRepository>();
builder.Services.AddSingleton<KafkaCoreCommandPublisher>();
builder.Services.AddScoped<CoreEventProcessor>();
builder.Services.AddHttpClient<GeminiIntentAnalyzer>();
builder.Services.AddHostedService<CoreDatabaseInitializer>();
builder.Services.AddHostedService<CoreEventConsumerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "core-service" }));

app.Run();
