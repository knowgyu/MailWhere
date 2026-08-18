namespace MailWhere.Storage;

internal static class MailMirrorSchema
{
    public const string TablesSql = """
        CREATE TABLE IF NOT EXISTS mail_messages (
            id INTEGER PRIMARY KEY,
            store_id TEXT NOT NULL,
            entry_id TEXT NOT NULL,
            open_source_token TEXT NULL,
            folder TEXT NOT NULL,
            received_at TEXT NULL,
            sent_at TEXT NULL,
            last_modified_at TEXT NOT NULL,
            conversation_id TEXT NULL,
            subject TEXT NOT NULL,
            sender_display TEXT NOT NULL,
            recipients_text TEXT NULL,
            body_text TEXT NOT NULL,
            body_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(store_id, entry_id)
        );

        CREATE TABLE IF NOT EXISTS mail_mirror_checkpoints (
            folder TEXT PRIMARY KEY,
            checkpoint TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS mail_mirror_meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS mail_mirror_generations (
            id TEXT PRIMARY KEY,
            folder TEXT NOT NULL,
            completed_at TEXT NOT NULL,
            seen_count INTEGER NOT NULL,
            deleted_count INTEGER NOT NULL
        );
        """;

    public const string IndexesSql = """
        CREATE INDEX IF NOT EXISTS idx_mail_messages_folder_time ON mail_messages(folder, received_at, sent_at);
        CREATE INDEX IF NOT EXISTS idx_mail_messages_conversation ON mail_messages(conversation_id);
        CREATE INDEX IF NOT EXISTS idx_mail_messages_modified ON mail_messages(last_modified_at, store_id, entry_id);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_mail_messages_open_source_token ON mail_messages(open_source_token) WHERE open_source_token IS NOT NULL;
        """;

    public static string FtsSql(string tokenizer) => $$"""
        CREATE VIRTUAL TABLE IF NOT EXISTS mail_messages_fts USING fts5(
            subject,
            sender_display,
            recipients_text,
            body_text,
            content='mail_messages',
            content_rowid='id',
            tokenize='{{tokenizer}}'
        );
        """;

    public const string TriggersSql = """
        CREATE TRIGGER IF NOT EXISTS mail_messages_ai AFTER INSERT ON mail_messages BEGIN
            INSERT INTO mail_messages_fts(rowid, subject, sender_display, recipients_text, body_text)
            VALUES (new.id, new.subject, new.sender_display, new.recipients_text, new.body_text);
        END;

        CREATE TRIGGER IF NOT EXISTS mail_messages_ad BEFORE DELETE ON mail_messages BEGIN
            INSERT INTO mail_messages_fts(mail_messages_fts, rowid, subject, sender_display, recipients_text, body_text)
            VALUES ('delete', old.id, old.subject, old.sender_display, old.recipients_text, old.body_text);
        END;

        CREATE TRIGGER IF NOT EXISTS mail_messages_au AFTER UPDATE ON mail_messages BEGIN
            INSERT INTO mail_messages_fts(mail_messages_fts, rowid, subject, sender_display, recipients_text, body_text)
            VALUES ('delete', old.id, old.subject, old.sender_display, old.recipients_text, old.body_text);
            INSERT INTO mail_messages_fts(rowid, subject, sender_display, recipients_text, body_text)
            VALUES (new.id, new.subject, new.sender_display, new.recipients_text, new.body_text);
        END;
        """;
}
