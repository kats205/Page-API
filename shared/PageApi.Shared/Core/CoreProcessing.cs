using System.Globalization;
using System.Text;
using PageApi.Shared.Kafka;

namespace PageApi.Shared.Core;

public sealed class CoreProcessingOptions
{
    public int RateLimitPerMinute { get; set; } = 20;
    public int RepeatedSpamThreshold { get; set; } = 3;
}

public sealed record ActorProcessingSnapshot(
    int EventsInLastMinute,
    int SpamEventsInLast24Hours,
    bool IsBlacklisted)
{
    public static ActorProcessingSnapshot Empty { get; } = new(0, 0, false);
}

public sealed record CoreDecision(
    string Intent,
    string Sentiment,
    string Action,
    string Status,
    string? ReplyText,
    bool IsSpam,
    bool ShouldBlacklist,
    string Reason)
{
    public bool RequiresManualReview { get; init; }
}

public sealed record AiClassification(string Intent, string Sentiment);

public sealed class CoreDecisionEngine
{
    private readonly CoreProcessingOptions _options;

    public CoreDecisionEngine(CoreProcessingOptions options)
    {
        _options = options;
    }

    public CoreDecision Decide(RawEventMessage rawEvent, ActorProcessingSnapshot actor, AiClassification? aiClassification = null)
    {
        var message = rawEvent.Message ?? string.Empty;
        var normalized = Normalize(message);

        if (actor.EventsInLastMinute >= _options.RateLimitPerMinute)
        {
            return new CoreDecision(
                Intent: "rate_limited",
                Sentiment: "neutral",
                Action: CommandAction.ManualReview,
                Status: EventProcessingStatus.PendingReview,
                ReplyText: null,
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "actor_rate_limit_exceeded")
            {
                RequiresManualReview = true
            };
        }

        var containsLink = normalized.Contains("http://", StringComparison.Ordinal)
            || normalized.Contains("https://", StringComparison.Ordinal)
            || normalized.Contains("www.", StringComparison.Ordinal);

        var suspicious = containsLink
            || ContainsAny(normalized, "scam", "bot", "nhan qua", "kiem tien", "telegram", "zalo.me");

        if (suspicious)
        {
            var shouldBlacklist = actor.SpamEventsInLast24Hours + 1 >= _options.RepeatedSpamThreshold;
            return new CoreDecision(
                Intent: "spam",
                Sentiment: "negative",
                Action: CommandAction.HideAndReview,
                Status: EventProcessingStatus.PendingReview,
                ReplyText: null,
                IsSpam: true,
                ShouldBlacklist: shouldBlacklist,
                Reason: containsLink ? "link_or_scam_detected" : "spam_keyword_detected")
            {
                RequiresManualReview = true
            };
        }

        if (actor.IsBlacklisted)
        {
            return new CoreDecision(
                Intent: "blacklisted_actor",
                Sentiment: "neutral",
                Action: CommandAction.None,
                Status: EventProcessingStatus.Processed,
                ReplyText: null,
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "actor_is_blacklisted");
        }

        if (ContainsAny(normalized, "gia", "bao nhieu", "tu van", "mua", "hoc phi"))
        {
            return new CoreDecision(
                Intent: "ask_price",
                Sentiment: "neutral",
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã quan tâm. Khanh Education sẽ gửi thông tin chi tiết cho bạn sớm.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "price_or_info_question");
        }

        if (ContainsAny(normalized, "chua nhan", "khieu nai", "khong ho tro", "ho tro cham", "ho tro te", "qua te", "te qua", "rat te", "khong tot", "chua tot", "loi", "that vong"))
        {
            return new CoreDecision(
                Intent: "complaint",
                Sentiment: "negative",
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Rất xin lỗi vì trải nghiệm chưa tốt. Bên mình sẽ kiểm tra ngay.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "complaint_apology_and_review")
            {
                RequiresManualReview = true
            };
        }

        if (ContainsAny(normalized, "hay qua", "tot", "cam on", "tuyet", "thich", "ho tro rat nhanh", "ho tro nhanh"))
        {
            return new CoreDecision(
                Intent: "praise",
                Sentiment: "positive",
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã ủng hộ Khanh Education.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "positive_engagement");
        }

        if (ContainsAny(normalized, "tam on", "tam duoc", "chap nhan"))
        {
            return new CoreDecision(
                Intent: "neutral_feedback",
                Sentiment: "neutral",
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã chia sẻ. Bên mình đã ghi nhận ý kiến của bạn.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "neutral_feedback_acknowledged");
        }

        if (aiClassification is not null)
        {
            return FromAiClassification(aiClassification);
        }

        return new CoreDecision(
            Intent: "unknown",
            Sentiment: "neutral",
            Action: CommandAction.ManualReview,
            Status: EventProcessingStatus.PendingReview,
            ReplyText: null,
            IsSpam: false,
            ShouldBlacklist: false,
            Reason: "fallback_manual_review");
    }

    private static CoreDecision FromAiClassification(AiClassification ai)
    {
        var intent = string.IsNullOrWhiteSpace(ai.Intent) ? "unknown" : ai.Intent;
        var sentiment = string.IsNullOrWhiteSpace(ai.Sentiment) ? "neutral" : ai.Sentiment;

        if (string.Equals(intent, "spam", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: "spam",
                Sentiment: sentiment,
                Action: CommandAction.HideAndReview,
                Status: EventProcessingStatus.PendingReview,
                ReplyText: null,
                IsSpam: true,
                ShouldBlacklist: false,
                Reason: "ai_spam")
            {
                RequiresManualReview = true
            };
        }

        if (string.Equals(intent, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: intent,
                Sentiment: sentiment,
                Action: CommandAction.ManualReview,
                Status: EventProcessingStatus.PendingReview,
                ReplyText: null,
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "ai_unknown_manual_review")
            {
                RequiresManualReview = true
            };
        }

        if (string.Equals(intent, "complaint", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sentiment, "negative", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: intent,
                Sentiment: sentiment,
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Rất xin lỗi vì trải nghiệm chưa tốt. Bên mình sẽ kiểm tra ngay.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "ai_apology_and_review")
            {
                RequiresManualReview = true
            };
        }

        if (string.Equals(intent, "ask_price", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: intent,
                Sentiment: sentiment,
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã quan tâm. Khanh Education sẽ gửi thông tin chi tiết cho bạn sớm.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "ai_price_or_info_question");
        }

        if (string.Equals(intent, "praise", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sentiment, "positive", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: intent,
                Sentiment: sentiment,
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã ủng hộ Khanh Education.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "ai_positive_engagement");
        }

        if (string.Equals(intent, "neutral_feedback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sentiment, "neutral", StringComparison.OrdinalIgnoreCase))
        {
            return new CoreDecision(
                Intent: intent,
                Sentiment: sentiment,
                Action: CommandAction.Reply,
                Status: EventProcessingStatus.Processed,
                ReplyText: "Cảm ơn bạn đã chia sẻ. Bên mình đã ghi nhận ý kiến của bạn.",
                IsSpam: false,
                ShouldBlacklist: false,
                Reason: "ai_neutral_feedback_acknowledged");
        }

        return new CoreDecision(
            Intent: intent,
            Sentiment: sentiment,
            Action: CommandAction.ManualReview,
            Status: EventProcessingStatus.PendingReview,
            ReplyText: null,
            IsSpam: false,
            ShouldBlacklist: false,
            Reason: "ai_unknown_manual_review");
    }

    private static bool ContainsAny(string input, params string[] terms)
    {
        return terms.Any(term => input.Contains(term, StringComparison.Ordinal));
    }

    private static string Normalize(string input)
    {
        var lower = input.Trim().ToLowerInvariant().Replace('đ', 'd');
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
