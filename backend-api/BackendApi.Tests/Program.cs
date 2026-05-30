using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Page_API.Authentication;
using Page_API.Models;
using Page_API.Services;

await TestRunner.RunAsync(
    ("ApiResponse wraps successful data consistently", ApiResponseWrapsSuccessfulData),
    ("ApiResponse wraps failures with codes", ApiResponseWrapsFailuresWithCodes),
    ("Admin API key authentication uses the expected header", AdminApiKeyUsesExpectedHeader),
    ("Command consumer defaults to Bai 2 Kafka topics", CommandConsumerDefaultsToBai2Topics),
    ("FacebookService retries transient Facebook errors", FacebookServiceRetriesTransientErrors),
    ("FacebookService does not retry invalid token errors", FacebookServiceDoesNotRetryInvalidTokenErrors));

static Task ApiResponseWrapsSuccessfulData()
{
    var response = ApiResponse<object>.Ok(new { id = "post_1" }, "Created");

    Assert.True(response.Success, "Success response should set Success=true.");
    Assert.Equal("Created", response.Message);
    Assert.NotNull(response.Data, "Success response should carry data.");
    Assert.Null(response.Error, "Success response should not carry an error.");

    return Task.CompletedTask;
}

static Task ApiResponseWrapsFailuresWithCodes()
{
    var response = ApiResponse<object>.Fail("Facebook token is invalid.", "FACEBOOK_TOKEN_INVALID");

    Assert.False(response.Success, "Failure response should set Success=false.");
    Assert.Null(response.Data, "Failure response should not carry data.");
    Assert.NotNull(response.Error, "Failure response should carry an error object.");
    Assert.Equal("FACEBOOK_TOKEN_INVALID", response.Error!.Code);

    return Task.CompletedTask;
}

static Task AdminApiKeyUsesExpectedHeader()
{
    Assert.Equal("X-Admin-Api-Key", AdminApiKeyAuthenticationOptions.DefaultHeaderName);
    return Task.CompletedTask;
}

static Task CommandConsumerDefaultsToBai2Topics()
{
    var options = new CommandConsumerOptions();

    Assert.True(options.Enabled, "Backend API should consume Bai 2 command topics by default.");
    Assert.Equal("reply_commands", options.ReplyCommandsTopic);
    Assert.Equal("send_retry", options.SendRetryTopic);
    Assert.Equal("send_failed", options.SendFailedTopic);

    return Task.CompletedTask;
}

static async Task FacebookServiceRetriesTransientErrors()
{
    var handler = new SequencedHttpMessageHandler(
        new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"message\":\"temporary\",\"code\":2}}", Encoding.UTF8, "application/json")
        },
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":\"post_1\"}", Encoding.UTF8, "application/json")
        });

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://graph.facebook.com/v19.0/")
    };

    var service = new FacebookService(
        client,
        new TestOptionsSnapshot<FacebookOptions>(new FacebookOptions
        {
            PageAccessToken = "token"
        }),
        new TestOptionsSnapshot<FacebookRetryOptions>(new FacebookRetryOptions
        {
            MaxRetries = 1,
            BaseDelayMilliseconds = 1
        }),
        NullLogger<FacebookService>.Instance);

    var result = await service.CreatePostAsync("page_1", new CreatePostRequest { Message = "hello" });

    Assert.NotNull(result, "CreatePostAsync should return the successful Graph API response.");
    Assert.Equal(2, handler.RequestCount);
}

static async Task FacebookServiceDoesNotRetryInvalidTokenErrors()
{
    var handler = new SequencedHttpMessageHandler(
        new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"invalid token\",\"code\":190}}", Encoding.UTF8, "application/json")
        });

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://graph.facebook.com/v19.0/")
    };

    var service = new FacebookService(
        client,
        new TestOptionsSnapshot<FacebookOptions>(new FacebookOptions
        {
            PageAccessToken = "token"
        }),
        new TestOptionsSnapshot<FacebookRetryOptions>(new FacebookRetryOptions
        {
            MaxRetries = 3,
            BaseDelayMilliseconds = 1
        }),
        NullLogger<FacebookService>.Instance);

    try
    {
        await service.CreatePostAsync("page_1", new CreatePostRequest { Message = "hello" });
        throw new InvalidOperationException("Expected FacebookApiException for invalid token.");
    }
    catch (FacebookApiException ex)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, ex.UpstreamStatusCode);
    }

    Assert.Equal(1, handler.RequestCount);
}

internal sealed class TestOptionsSnapshot<T> : IOptionsSnapshot<T>
    where T : class
{
    public TestOptionsSnapshot(T value)
    {
        Value = value;
    }

    public T Value { get; }

    public T Get(string? name) => Value;
}

internal sealed class SequencedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public SequencedHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response was configured.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}

internal static class TestRunner
{
    public static async Task RunAsync(params (string Name, Func<Task> Test)[] tests)
    {
        foreach (var (name, test) in tests)
        {
            await test();
            Console.WriteLine($"PASS {name}");
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void NotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Null(object? value, string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException(message);
        }
    }
}
