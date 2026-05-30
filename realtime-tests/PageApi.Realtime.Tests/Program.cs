using PageApi.Shared.Core;
using PageApi.Shared.Kafka;
using PageApi.Shared.Retry;
using Newtonsoft.Json.Linq;
using WebhookService.Services;

await TestRunner.RunAsync(
    ("Kafka topics match Bài 2 contract", KafkaTopicsMatchContract),
    ("Raw event uses stable comment idempotency key", RawEventUsesStableCommentKey),
    ("Decision engine classifies assignment sample messages", DecisionEngineClassifiesSamples),
    ("Decision engine maps Bai 3 sentiment samples to automation", DecisionEngineMapsBai3SentimentSamples),
    ("Decision engine sends unknown messages to manual review", DecisionEngineDoesNotReplyToUnknown),
    ("Decision engine keeps rate-limit setup comments manual before threshold", DecisionEngineKeepsRateTestCommentsManualBeforeLimit),
    ("Decision engine sends rate-limited actors to pending review", DecisionEngineRateLimitsActors),
    ("Webhook normalizer supports comments and messages", WebhookNormalizerSupportsCommentsAndMessages),
    ("Webhook normalizer ignores page-authored events", WebhookNormalizerIgnoresPageAuthoredEvents),
    ("Circuit breaker opens after consecutive failures", CircuitBreakerOpensAfterFailures),
    ("Retry planner uses 1s 2s 4s and then dead letter", RetryPlannerUsesExpectedBackoff));

static Task KafkaTopicsMatchContract()
{
    Assert.Equal("raw_events", KafkaTopics.RawEvents);
    Assert.Equal("reply_commands", KafkaTopics.ReplyCommands);
    Assert.Equal("send_retry", KafkaTopics.SendRetry);
    Assert.Equal("send_failed", KafkaTopics.SendFailed);
    Assert.Equal("dead_letter", KafkaTopics.DeadLetter);
    return Task.CompletedTask;
}

static Task RawEventUsesStableCommentKey()
{
    var raw = new RawEventMessage
    {
        PageId = "page_1",
        PostId = "post_1",
        CommentId = "comment_1",
        UserId = "actor_1",
        Message = "Shop oi gia bao nhieu?"
    };

    raw.EnsureIdentity();

    Assert.Equal("facebook:comment:comment_1", raw.EventId);
    Assert.Equal("comment_created", raw.EventType);
    Assert.Equal("facebook", raw.Source);
    return Task.CompletedTask;
}

static Task DecisionEngineClassifiesSamples()
{
    var engine = new CoreDecisionEngine(new CoreProcessingOptions());

    var price = engine.Decide(MakeEvent("Shop oi gia bao nhieu?", "actor_1"), ActorProcessingSnapshot.Empty);
    Assert.Equal("ask_price", price.Intent);
    Assert.Equal("neutral", price.Sentiment);
    Assert.Equal(CommandAction.Reply, price.Action);

    var complaint = engine.Decide(MakeEvent("Minh chua nhan duoc hang", "actor_2"), ActorProcessingSnapshot.Empty);
    Assert.Equal("complaint", complaint.Intent);
    Assert.Equal("negative", complaint.Sentiment);
    Assert.Equal(CommandAction.Reply, complaint.Action);
    Assert.True(complaint.RequiresManualReview, "Complaints should be reviewed after the apology reply.");

    var praise = engine.Decide(MakeEvent("Bai viet hay qua", "actor_3"), ActorProcessingSnapshot.Empty);
    Assert.Equal("praise", praise.Intent);
    Assert.Equal("positive", praise.Sentiment);
    Assert.Equal(CommandAction.Reply, praise.Action);

    var spam = engine.Decide(MakeEvent("Nhan qua tai http://scam.test", "actor_4"), ActorProcessingSnapshot.Empty);
    Assert.Equal("spam", spam.Intent);
    Assert.Equal(CommandAction.HideAndReview, spam.Action);
    return Task.CompletedTask;
}

static Task DecisionEngineMapsBai3SentimentSamples()
{
    var engine = new CoreDecisionEngine(new CoreProcessingOptions());

    var positive = engine.Decide(MakeEvent("Dịch vụ rất tốt, mình sẽ quay lại", "actor_10"), ActorProcessingSnapshot.Empty);
    Assert.Equal("praise", positive.Intent);
    Assert.Equal("positive", positive.Sentiment);
    Assert.Equal(CommandAction.Reply, positive.Action);
    Assert.Equal("Cảm ơn bạn đã ủng hộ Khanh Education.", positive.ReplyText);
    Assert.False(positive.RequiresManualReview, "Positive comments should not require review.");

    var fastSupport = engine.Decide(MakeEvent("Shop ho tro rat nhanh", "actor_14"), ActorProcessingSnapshot.Empty);
    Assert.Equal("praise", fastSupport.Intent);
    Assert.Equal("positive", fastSupport.Sentiment);
    Assert.Equal(CommandAction.Reply, fastSupport.Action);

    var neutral = engine.Decide(MakeEvent("Sản phẩm tạm ổn", "actor_11"), ActorProcessingSnapshot.Empty);
    Assert.Equal("neutral_feedback", neutral.Intent);
    Assert.Equal("neutral", neutral.Sentiment);
    Assert.Equal(CommandAction.Reply, neutral.Action);
    Assert.Equal("Cảm ơn bạn đã chia sẻ. Bên mình đã ghi nhận ý kiến của bạn.", neutral.ReplyText);
    Assert.False(neutral.RequiresManualReview, "Neutral comments should be acknowledged without review.");

    var negative = engine.Decide(MakeEvent("Trải nghiệm quá tệ", "actor_12"), ActorProcessingSnapshot.Empty);
    Assert.Equal("complaint", negative.Intent);
    Assert.Equal("negative", negative.Sentiment);
    Assert.Equal(CommandAction.Reply, negative.Action);
    Assert.Equal("Rất xin lỗi vì trải nghiệm chưa tốt. Bên mình sẽ kiểm tra ngay.", negative.ReplyText);
    Assert.True(negative.RequiresManualReview, "Negative comments should be reviewed after the apology reply.");

    var spam = engine.Decide(MakeEvent("Quảng cáo lặp lại http://spam-example.test", "actor_13"), ActorProcessingSnapshot.Empty);
    Assert.Equal("spam", spam.Intent);
    Assert.Equal("negative", spam.Sentiment);
    Assert.Equal(CommandAction.HideAndReview, spam.Action);
    Assert.True(spam.RequiresManualReview, "Spam comments should be hidden and reviewed.");

    return Task.CompletedTask;
}

static Task DecisionEngineRateLimitsActors()
{
    var engine = new CoreDecisionEngine(new CoreProcessingOptions { RateLimitPerMinute = 20 });
    var decision = engine.Decide(
        MakeEvent("Shop oi tu van giup", "actor_1"),
        new ActorProcessingSnapshot(EventsInLastMinute: 20, SpamEventsInLast24Hours: 0, IsBlacklisted: false));

    Assert.Equal(EventProcessingStatus.PendingReview, decision.Status);
    Assert.Equal(CommandAction.ManualReview, decision.Action);
    return Task.CompletedTask;
}

static Task DecisionEngineDoesNotReplyToUnknown()
{
    var engine = new CoreDecisionEngine(new CoreProcessingOptions());
    var decision = engine.Decide(MakeEvent("Noi dung binh thuong khong ro y dinh", "actor_5"), ActorProcessingSnapshot.Empty);

    Assert.Equal("unknown", decision.Intent);
    Assert.Equal(CommandAction.ManualReview, decision.Action);
    Assert.Equal(EventProcessingStatus.PendingReview, decision.Status);
    return Task.CompletedTask;
}

static Task DecisionEngineKeepsRateTestCommentsManualBeforeLimit()
{
    var engine = new CoreDecisionEngine(new CoreProcessingOptions { RateLimitPerMinute = 20 });
    var decision = engine.Decide(MakeEvent("rate test 01", "actor_rate"), ActorProcessingSnapshot.Empty);

    Assert.Equal("unknown", decision.Intent);
    Assert.Equal("neutral", decision.Sentiment);
    Assert.Equal(CommandAction.ManualReview, decision.Action);
    Assert.Equal(EventProcessingStatus.PendingReview, decision.Status);
    Assert.Equal<string?>(null, decision.ReplyText);

    var aiUnknown = engine.Decide(
        MakeEvent("rate test 02", "actor_rate"),
        ActorProcessingSnapshot.Empty,
        new AiClassification("unknown", "neutral"));

    Assert.Equal("unknown", aiUnknown.Intent);
    Assert.Equal("neutral", aiUnknown.Sentiment);
    Assert.Equal(CommandAction.ManualReview, aiUnknown.Action);
    Assert.Equal(EventProcessingStatus.PendingReview, aiUnknown.Status);
    Assert.Equal<string?>(null, aiUnknown.ReplyText);

    return Task.CompletedTask;
}

static Task RetryPlannerUsesExpectedBackoff()
{
    var planner = new RetryPlanner(new RetryOptions { MaxAttempts = 3 });

    var first = planner.Plan(0);
    Assert.False(first.SendToDeadLetter, "retry_count 0 should retry");
    Assert.Equal(TimeSpan.FromSeconds(1), first.Delay);
    Assert.Equal(1, first.NextRetryCount);

    var third = planner.Plan(2);
    Assert.False(third.SendToDeadLetter, "retry_count 2 should retry once more");
    Assert.Equal(TimeSpan.FromSeconds(4), third.Delay);
    Assert.Equal(3, third.NextRetryCount);

    var exhausted = planner.Plan(3);
    Assert.True(exhausted.SendToDeadLetter, "retry_count 3 should dead-letter");
    return Task.CompletedTask;
}

static Task CircuitBreakerOpensAfterFailures()
{
    var breaker = new FacebookCircuitBreaker(new FacebookCircuitBreakerOptions
    {
        FailureThreshold = 5,
        OpenSeconds = 30
    });

    for (var i = 0; i < 5; i++)
    {
        Assert.True(breaker.CanExecute(DateTimeOffset.UtcNow), "Breaker should allow calls before threshold.");
        breaker.RecordFailure(DateTimeOffset.UtcNow);
    }

    Assert.False(breaker.CanExecute(DateTimeOffset.UtcNow), "Breaker should block after threshold.");
    Assert.True(breaker.CanExecute(DateTimeOffset.UtcNow.AddSeconds(31)), "Breaker should allow half-open probe after open window.");
    breaker.RecordSuccess();
    Assert.True(breaker.CanExecute(DateTimeOffset.UtcNow), "Breaker should close after success.");

    return Task.CompletedTask;
}

static Task WebhookNormalizerSupportsCommentsAndMessages()
{
    var normalizer = new FacebookEventNormalizer();
    var commentPayload = JToken.Parse("""
        {
          "object": "page",
          "entry": [{
            "id": "page_1",
            "changes": [{
              "field": "feed",
              "value": {
                "item": "comment",
                "verb": "add",
                "comment_id": "comment_1",
                "post_id": "post_1",
                "sender_id": "actor_1",
                "message": "Shop oi gia bao nhieu?",
                "created_time": 1716700000
              }
            }]
          }]
        }
        """);

    var messagePayload = JToken.Parse("""
        {
          "object": "page",
          "entry": [{
            "id": "page_1",
            "messaging": [{
              "sender": { "id": "actor_2" },
              "recipient": { "id": "page_1" },
              "timestamp": 1716700000000,
              "message": {
                "mid": "message_1",
                "text": "Minh chua nhan duoc hang"
              }
            }]
          }]
        }
        """);

    var comments = normalizer.NormalizeEvents(commentPayload, "page_1");
    Assert.Equal(1, comments.Count);
    Assert.Equal("facebook:comment:comment_1", comments[0].EventId);
    Assert.Equal("comment_created", comments[0].EventType);

    var messages = normalizer.NormalizeEvents(messagePayload, "page_1");
    Assert.Equal(1, messages.Count);
    Assert.Equal("facebook:message:message_1", messages[0].EventId);
    Assert.Equal("message_created", messages[0].EventType);
    Assert.Equal("actor_2", messages[0].UserId);

    return Task.CompletedTask;
}

static Task WebhookNormalizerIgnoresPageAuthoredEvents()
{
    var normalizer = new FacebookEventNormalizer();
    var pageAuthoredCommentPayload = JToken.Parse("""
        {
          "object": "page",
          "entry": [{
            "id": "page_1",
            "changes": [{
              "field": "feed",
              "value": {
                "item": "comment",
                "verb": "add",
                "comment_id": "comment_2",
                "post_id": "post_1",
                "sender_id": "page_1",
                "message": "Cam on ban da ung ho Khanh Education.",
                "created_time": 1716700000
              }
            }]
          }]
        }
        """);

    var pageAuthoredMessagePayload = JToken.Parse("""
        {
          "object": "page",
          "entry": [{
            "id": "page_1",
            "messaging": [{
              "sender": { "id": "page_1" },
              "recipient": { "id": "actor_2" },
              "timestamp": 1716700000000,
              "message": {
                "mid": "message_2",
                "text": "Cam on ban da lien he."
              }
            }]
          }]
        }
        """);

    Assert.Equal(0, normalizer.NormalizeEvents(pageAuthoredCommentPayload, "page_1").Count);
    Assert.Equal(0, normalizer.NormalizeEvents(pageAuthoredMessagePayload, "page_1").Count);
    return Task.CompletedTask;
}

static RawEventMessage MakeEvent(string message, string actorId)
{
    var raw = new RawEventMessage
    {
        PageId = "page_1",
        PostId = "post_1",
        CommentId = Guid.NewGuid().ToString("N"),
        UserId = actorId,
        Message = message
    };
    raw.EnsureIdentity();
    return raw;
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
}
