using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PageApi.Shared.Kafka;

namespace WebhookService.Services;

public class FacebookEventNormalizer
{
    public IReadOnlyList<RawEventMessage> NormalizeEvents(JToken payload, string? expectedPageId)
    {
        var result = new List<RawEventMessage>();
        if (payload is not JObject root)
        {
            return result;
        }

        var objectType = root.Value<string>("object");
        if (!string.Equals(objectType, "page", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var entries = root["entry"] as JArray;
        if (entries is null)
        {
            return result;
        }

        foreach (var entry in entries.OfType<JObject>())
        {
            var pageId = entry.Value<string>("id") ?? string.Empty;
            if (!IsExpectedPage(pageId, expectedPageId))
            {
                continue;
            }

            NormalizeFeedChanges(entry, pageId, receivedAt, result);
            NormalizeMessagingEvents(entry, pageId, receivedAt, result);
        }

        return result;
    }

    private static void NormalizeFeedChanges(
        JObject entry,
        string pageId,
        DateTimeOffset receivedAt,
        List<RawEventMessage> result)
    {
        var changes = entry["changes"] as JArray;
        if (changes is null)
        {
            return;
        }

        foreach (var change in changes.OfType<JObject>())
        {
            if (!string.Equals(change.Value<string>("field"), "feed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = change["value"] as JObject;
            if (value is null
                || !string.Equals(value.Value<string>("item"), "comment", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(value.Value<string>("verb"), "add", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = new RawEventMessage
            {
                PageId = pageId,
                PostId = value.Value<string>("post_id"),
                CommentId = value.Value<string>("comment_id"),
                UserId = value.Value<string>("sender_id") ?? value["from"]?["id"]?.ToString(),
                Message = value.Value<string>("message"),
                CreatedAt = ParseCreatedAt(value["created_time"]) ?? receivedAt,
                ReceivedAt = receivedAt,
                PayloadJson = change.ToString(Formatting.None)
            };
            raw.EnsureIdentity();
            result.Add(raw);
        }
    }

    private static void NormalizeMessagingEvents(
        JObject entry,
        string pageId,
        DateTimeOffset receivedAt,
        List<RawEventMessage> result)
    {
        var messaging = entry["messaging"] as JArray;
        if (messaging is null)
        {
            return;
        }

        foreach (var messageEvent in messaging.OfType<JObject>())
        {
            var message = messageEvent["message"] as JObject;
            var text = message?.Value<string>("text");
            var messageId = message?.Value<string>("mid");
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(messageId))
            {
                continue;
            }

            var raw = new RawEventMessage
            {
                PageId = pageId,
                MessageId = messageId,
                UserId = messageEvent["sender"]?["id"]?.ToString(),
                Message = text,
                CreatedAt = ParseCreatedAt(messageEvent["timestamp"]) ?? receivedAt,
                ReceivedAt = receivedAt,
                PayloadJson = messageEvent.ToString(Formatting.None)
            };
            raw.EnsureIdentity();
            result.Add(raw);
        }
    }

    private static bool IsExpectedPage(string pageId, string? expectedPageId)
    {
        return string.IsNullOrWhiteSpace(expectedPageId)
            || string.Equals(pageId, expectedPageId, StringComparison.Ordinal);
    }

    private static DateTimeOffset? ParseCreatedAt(JToken? token)
    {
        if (token is null)
        {
            return null;
        }

        if (token.Type == JTokenType.Integer && token.Value<long?>() is long intValue)
        {
            return intValue > 9999999999
                ? DateTimeOffset.FromUnixTimeMilliseconds(intValue)
                : DateTimeOffset.FromUnixTimeSeconds(intValue);
        }

        var text = token.ToString();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixValue))
        {
            return unixValue > 9999999999
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixValue)
                : DateTimeOffset.FromUnixTimeSeconds(unixValue);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
