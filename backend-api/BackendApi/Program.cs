using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Page_API.Authentication;
using Page_API.Models;
using Page_API.Services;
using PageApi.Shared.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

            return new BadRequestObjectResult(
                ApiResponse<object>.Fail("Validation failed.", "VALIDATION_ERROR", errors));
        };
    });

// Configure Facebook Options
builder.Services.Configure<Page_API.Models.FacebookOptions>(builder.Configuration.GetSection("Facebook"));
builder.Services.Configure<Page_API.Models.FacebookRetryOptions>(builder.Configuration.GetSection("FacebookRetry"));
builder.Services.Configure<CommandConsumerOptions>(builder.Configuration.GetSection("CommandConsumer"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<FacebookCircuitBreakerOptions>(builder.Configuration.GetSection("FacebookCircuitBreaker"));

// Register Facebook Service and HttpClient
builder.Services.AddHttpClient<Page_API.Services.IFacebookService, Page_API.Services.FacebookService>(client =>
{
    var baseUrl = builder.Configuration.GetValue<string>("Facebook:BaseUrl") ?? "https://graph.facebook.com/v19.0/";
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
});

// Configure lowercase URLs
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddSingleton<CommandStateRepository>();
builder.Services.AddSingleton<KafkaFailedCommandPublisher>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FacebookCircuitBreakerOptions>>().Value;
    return new FacebookCircuitBreaker(options);
});
builder.Services.AddScoped<FacebookCommandHandler>();
builder.Services.AddHostedService<BackendDatabaseInitializer>();
builder.Services.AddHostedService<FacebookCommandConsumerService>();

builder.Services
    .AddAuthentication(AdminApiKeyAuthenticationOptions.Scheme)
    .AddScheme<AdminApiKeyAuthenticationOptions, AdminApiKeyAuthenticationHandler>(
        AdminApiKeyAuthenticationOptions.Scheme,
        options => builder.Configuration.GetSection("AdminAuth").Bind(options));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicies.AdminOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(AdminApiKeyAuthenticationOptions.Scheme, new OpenApiSecurityScheme
    {
        Description = "Admin API key. Enter only the key value; Swagger sends it as X-Admin-Api-Key.",
        Name = AdminApiKeyAuthenticationOptions.DefaultHeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = AdminApiKeyAuthenticationOptions.Scheme
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = AdminApiKeyAuthenticationOptions.Scheme
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
