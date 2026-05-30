namespace PageApi.Shared.Data;

public static class PageApiSchema
{
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS event_states (
            event_id TEXT PRIMARY KEY,
            event_type TEXT NOT NULL,
            source TEXT NOT NULL,
            page_id TEXT NOT NULL,
            post_id TEXT NULL,
            comment_id TEXT NULL,
            user_id TEXT NULL,
            message TEXT NULL,
            intent TEXT NULL,
            sentiment TEXT NULL,
            status TEXT NOT NULL,
            reason TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL,
            received_at TIMESTAMPTZ NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS processed_commands (
            command_id TEXT NOT NULL,
            action TEXT NOT NULL,
            event_id TEXT NOT NULL,
            processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            status TEXT NOT NULL,
            PRIMARY KEY (command_id, action)
        );

        CREATE TABLE IF NOT EXISTS actor_activity (
            id BIGSERIAL PRIMARY KEY,
            actor_id TEXT NOT NULL,
            event_id TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS actor_spam_events (
            id BIGSERIAL PRIMARY KEY,
            actor_id TEXT NOT NULL,
            event_id TEXT NOT NULL,
            reason TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS blacklisted_actors (
            actor_id TEXT PRIMARY KEY,
            reason TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS manual_review_items (
            id BIGSERIAL PRIMARY KEY,
            event_id TEXT NOT NULL,
            actor_id TEXT NULL,
            reason TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            resolved_at TIMESTAMPTZ NULL
        );

        CREATE TABLE IF NOT EXISTS failed_commands (
            id BIGSERIAL PRIMARY KEY,
            command_id TEXT NOT NULL,
            event_id TEXT NOT NULL,
            retry_count INTEGER NOT NULL,
            last_error TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            status TEXT NOT NULL,
            next_retry_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_actor_activity_actor_created
            ON actor_activity(actor_id, created_at);

        CREATE INDEX IF NOT EXISTS idx_actor_spam_actor_created
            ON actor_spam_events(actor_id, created_at);
        """;
}
