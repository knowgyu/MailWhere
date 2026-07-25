namespace MailWhere.Core.Capabilities;

public static class MailMirrorDiagnostics
{
    public const string SqliteProbeId = "mail-mirror-sqlite";
    public const string Fts5ProbeId = "mail-mirror-fts5";
    public const string TokenizerProbeId = "mail-mirror-tokenizer";
    public const string OutlookInventoryProbeId = "mail-mirror-outlook-inventory";

    public static readonly string[] ContentFreeMetricKeys =
    [
        "durationMs",
        "elapsedMs",
        "p50Ms",
        "p95Ms",
        "batchSize",
        "pageSize",
        "rowCount",
        "hitCount",
        "failureCount",
        "fallbackCount",
        "tokenizer",
        "journalMode",
        "connectionMode",
        "operation"
    ];

    public static readonly string[] ForbiddenDetailKeys =
    [
        "body",
        "subject",
        "html",
        "rtf",
        "entryId",
        "storeId",
        "senderAddress",
        "recipientAddress",
        "sourceId",
        "sourceIdHash"
    ];
}
