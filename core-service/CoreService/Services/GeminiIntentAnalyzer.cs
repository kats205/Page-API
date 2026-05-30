using System.Net.Http.Json;
using System.Text.Json;
using CoreService.Models;
using Microsoft.Extensions.Options;
using PageApi.Shared.Core;
using PageApi.Shared.Kafka;

namespace CoreService.Services;

public sealed class GeminiIntentAnalyzer
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly FacebookCircuitBreaker _circuitBreaker;
    private readonly ILogger<GeminiIntentAnalyzer> _logger;

    public GeminiIntentAnalyzer(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        FacebookCircuitBreaker circuitBreaker,
        ILogger<GeminiIntentAnalyzer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public async Task<AiClassification?> AnalyzeAsync(RawEventMessage rawEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(rawEvent.Message))
        {
            return null;
        }

        if (!_circuitBreaker.CanExecute(DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Gemini circuit breaker is open. EventId={EventId}. Using fallback rules.", rawEvent.EventId);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        try
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent";
            var request = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = "Classify this Facebook page comment/message. Return only JSON with intent and sentiment. Allowed intents: ask_price, complaint, praise, neutral_feedback, spam, unknown. Allowed sentiments: positive, neutral, negative. Text: " + rawEvent.Message
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    responseMimeType = "application/json"
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

            var response = await _httpClient.SendAsync(httpRequest, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeout.Token);
                if (IsQuotaError((int)response.StatusCode, errorBody))
                {
                    _logger.LogWarning(
                        "Gemini quota or rate limit reached. StatusCode={StatusCode} Model={Model} EventId={EventId}. Using fallback rules. Error={ErrorBody}",
                        (int)response.StatusCode,
                        _options.Model,
                        rawEvent.EventId,
                        Truncate(errorBody, 500));
                }
                else
                {
                    _logger.LogWarning(
                        "Gemini returned non-success status {StatusCode}. Model={Model} EventId={EventId}. Using fallback rules. Error={ErrorBody}",
                        (int)response.StatusCode,
                        _options.Model,
                        rawEvent.EventId,
                        Truncate(errorBody, 500));
                }

                _circuitBreaker.RecordFailure(DateTimeOffset.UtcNow);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(body);
            var text = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            var classification = ParseClassification(text);
            if (classification is null)
            {
                _circuitBreaker.RecordFailure(DateTimeOffset.UtcNow);
                return null;
            }

            _circuitBreaker.RecordSuccess();
            return classification;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _circuitBreaker.RecordFailure(DateTimeOffset.UtcNow);
            _logger.LogWarning(ex, "Gemini analysis failed; using fallback.");
            return null;
        }
    }

    private static AiClassification? ParseClassification(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < jsonStart)
        {
            return null;
        }

        var json = text[jsonStart..(jsonEnd + 1)];
        using var document = JsonDocument.Parse(json);
        var intent = document.RootElement.TryGetProperty("intent", out var intentElement)
            ? intentElement.GetString()
            : null;
        var sentiment = document.RootElement.TryGetProperty("sentiment", out var sentimentElement)
            ? sentimentElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(intent) && string.IsNullOrWhiteSpace(sentiment))
        {
            return null;
        }

        return new AiClassification(intent ?? "unknown", sentiment ?? "neutral");
    }

    private static bool IsQuotaError(int statusCode, string errorBody)
    {
        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            return true;
        }

        return errorBody.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("rate_limit", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
