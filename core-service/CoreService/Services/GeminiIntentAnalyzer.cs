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
    private readonly ILogger<GeminiIntentAnalyzer> _logger;

    public GeminiIntentAnalyzer(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiIntentAnalyzer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiClassification?> AnalyzeAsync(RawEventMessage rawEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(rawEvent.Message))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        try
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}";
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
                                text = "Classify this Facebook page comment/message. Return only JSON with intent and sentiment. Allowed intents: ask_price, complaint, praise, spam, unknown. Allowed sentiments: positive, neutral, negative. Text: " + rawEvent.Message
                            }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(endpoint, request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini returned non-success status {StatusCode}; using fallback.", (int)response.StatusCode);
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

            return ParseClassification(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
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
}
