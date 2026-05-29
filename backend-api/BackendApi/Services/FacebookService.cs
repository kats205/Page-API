using Microsoft.Extensions.Options;
using Page_API.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Page_API.Services
{
    public class FacebookService : IFacebookService
    {
        private readonly HttpClient _httpClient;
        private readonly FacebookOptions _options;
        private readonly FacebookRetryOptions _retryOptions;
        private readonly ILogger<FacebookService> _logger;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookService(
            HttpClient httpClient,
            IOptionsSnapshot<FacebookOptions> options,
            IOptionsSnapshot<FacebookRetryOptions> retryOptions,
            ILogger<FacebookService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _retryOptions = retryOptions.Value;
            _logger = logger;
        }

        private static async Task ThrowFacebookApiException(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();

            FacebookApiError? facebookError = null;
            try
            {
                var envelope = JsonSerializer.Deserialize<FacebookApiErrorEnvelope>(body, JsonOptions);
                facebookError = envelope?.Error;
            }
            catch
            {
                // Ignore parsing errors; fall back to raw body.
            }

            var message = facebookError?.Message ?? body;
            throw new FacebookApiException(response.StatusCode, facebookError, body, $"Facebook API Error: {(int)response.StatusCode} - {message}");
        }

        public async Task<object?> GetPageInfoAsync(string pageId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{pageId}?access_token={EncodeToken()}"),
                "get_page_info",
                pageId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> GetPostsAsync(string pageId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{pageId}/posts?access_token={EncodeToken()}"),
                "get_posts",
                pageId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> CreatePostAsync(string pageId, CreatePostRequest request)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, $"{pageId}/feed")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["message"] = request.Message,
                        ["access_token"] = _options.PageAccessToken
                    })
                },
                "create_post",
                pageId,
                request.Message);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> ReplyToCommentAsync(string commentId, string message)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, $"{commentId}/comments")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["message"] = message,
                        ["access_token"] = _options.PageAccessToken
                    })
                },
                "reply_comment",
                commentId,
                message);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }

            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> HideCommentAsync(string commentId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, commentId)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["is_hidden"] = "true",
                        ["access_token"] = _options.PageAccessToken
                    })
                },
                "hide_comment",
                commentId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }

            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<bool> DeletePostAsync(string postId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Delete, $"{postId}?access_token={EncodeToken()}"),
                "delete_post",
                postId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<object?> GetCommentsAsync(string postId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{postId}/comments?access_token={EncodeToken()}"),
                "get_comments",
                postId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> GetLikesAsync(string postId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{postId}/likes?access_token={EncodeToken()}"),
                "get_likes",
                postId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        public async Task<object?> GetInsightsAsync(string pageId)
        {
            var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{pageId}/insights?metric=page_views_total&access_token={EncodeToken()}"),
                "get_insights",
                pageId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>(JsonOptions);
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<HttpRequestMessage> requestFactory,
            string operation,
            string targetId,
            string? payloadPreview = null)
        {
            var maxRetries = Math.Max(0, _retryOptions.MaxRetries);
            for (var attempt = 0; ; attempt++)
            {
                using var request = requestFactory();
                var stopwatch = Stopwatch.StartNew();

                _logger.LogInformation(
                    "Sending Facebook request. Operation={Operation} Method={Method} Path={Path} TargetId={TargetId} Attempt={Attempt} PayloadPreview={PayloadPreview}",
                    operation,
                    request.Method,
                    request.RequestUri?.ToString(),
                    targetId,
                    attempt + 1,
                    TrimPayload(payloadPreview));

                try
                {
                    var response = await _httpClient.SendAsync(request);
                    stopwatch.Stop();

                    _logger.LogInformation(
                        "Received Facebook response. Operation={Operation} StatusCode={StatusCode} TargetId={TargetId} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                        operation,
                        (int)response.StatusCode,
                        targetId,
                        attempt + 1,
                        stopwatch.ElapsedMilliseconds);

                    if (response.IsSuccessStatusCode
                        || !ShouldRetry(response.StatusCode)
                        || attempt >= maxRetries)
                    {
                        return response;
                    }

                    response.Dispose();
                }
                catch (Exception ex) when (IsTransientException(ex))
                {
                    stopwatch.Stop();
                    if (attempt >= maxRetries)
                    {
                        _logger.LogError(
                            ex,
                            "Facebook request failed after retries. Operation={Operation} TargetId={TargetId} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                            operation,
                            targetId,
                            attempt + 1,
                            stopwatch.ElapsedMilliseconds);

                        throw new FacebookApiException(
                            HttpStatusCode.ServiceUnavailable,
                            null,
                            ex.Message,
                            $"Facebook API request failed after {attempt + 1} attempt(s): {ex.Message}");
                    }

                    _logger.LogWarning(
                        ex,
                        "Facebook request failed transiently. Operation={Operation} TargetId={TargetId} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                        operation,
                        targetId,
                        attempt + 1,
                        stopwatch.ElapsedMilliseconds);
                }

                await Task.Delay(GetRetryDelay(attempt));
            }
        }

        private string EncodeToken()
        {
            return WebUtility.UrlEncode(_options.PageAccessToken);
        }

        private TimeSpan GetRetryDelay(int attempt)
        {
            var baseDelay = Math.Max(1, _retryOptions.BaseDelayMilliseconds);
            return TimeSpan.FromMilliseconds(baseDelay * Math.Pow(2, attempt));
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout
                || statusCode == (HttpStatusCode)429
                || (int)statusCode >= 500;
        }

        private static bool IsTransientException(Exception exception)
        {
            return exception is HttpRequestException or TaskCanceledException;
        }

        private static string? TrimPayload(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return payload;
            }

            return payload.Length <= 120 ? payload : payload[..120];
        }
    }
}
