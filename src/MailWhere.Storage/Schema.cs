namespace MailWhere.Storage;

internal static class Schema
{
    public const string TablesSql = """
        CREATE TABLE IF NOT EXISTS tasks (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            due_at TEXT NULL,
            source_id_hash TEXT NULL,
            source_id TEXT NULL,
            confidence REAL NOT NULL,
            reason TEXT NOT NULL,
            evidence_snippet TEXT NULL,
            status TEXT NOT NULL,
            snooze_until TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            source_derived_data_deleted INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS review_candidates (
            id TEXT PRIMARY KEY,
            source_id_hash TEXT NOT NULL,
            source_id TEXT NULL,
            kind TEXT NOT NULL,
            confidence REAL NOT NULL,
            suggested_title TEXT NOT NULL,
            reason TEXT NOT NULL,
            evidence_snippet TEXT NULL,
            due_at TEXT NULL,
            created_at TEXT NOT NULL,
            snooze_until TEXT NULL,
            suppressed INTEGER NOT NULL DEFAULT 0,
            resolved_at TEXT NULL,
            resolution TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS processed_sources (
            source_id_hash TEXT PRIMARY KEY,
            processed_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS app_state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        """;

    public const string IndexesSql = """
        CREATE INDEX IF NOT EXISTS idx_tasks_status_due ON tasks(status, due_at);
        CREATE INDEX IF NOT EXISTS idx_tasks_source_hash ON tasks(source_id_hash);
        CREATE INDEX IF NOT EXISTS idx_review_source_hash ON review_candidates(source_id_hash);
        CREATE INDEX IF NOT EXISTS idx_review_active ON review_candidates(suppressed, resolved_at, created_at);
        CREATE INDEX IF NOT EXISTS idx_review_active_snooze ON review_candidates(suppressed, resolved_at, snooze_until, created_at);
        """;

    public const string Sql = TablesSql + IndexesSql;
}
