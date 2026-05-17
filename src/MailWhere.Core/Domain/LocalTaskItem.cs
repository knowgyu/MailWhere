namespace MailWhere.Core.Domain;

public enum LocalTaskStatus
{
    Open,
    Snoozed,
    Archived,
    Done,
    Dismissed,
    NotATask
}

public sealed record LocalTaskItem(
    Guid Id,
    string Title,
    DateTimeOffset? DueAt,
    string? SourceIdHash,
    string? SourceId,
    double Confidence,
    string Reason,
    string? EvidenceSnippet,
    LocalTaskStatus Status,
    DateTimeOffset? SnoozeUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool SourceDerivedDataDeleted = false,
    string? SourceSenderDisplay = null,
    DateTimeOffset? SourceReceivedAt = null,
    MailboxRecipientRole SourceRecipientRole = MailboxRecipientRole.Direct,
    FollowUpKind Kind = FollowUpKind.ActionRequested,
    string? SourceConversationId = null,
    IReadOnlyList<string>? SourceRecipientDisplayNames = null)
{
    public const string RedactedTitle = "메일 기반 항목(원문 삭제됨)";
    public const string RedactedReason = "메일 원문 기반 사유가 삭제되었습니다.";

    public static LocalTaskItem FromAnalysis(EmailSnapshot source, FollowUpAnalysis analysis, DateTimeOffset now)
    {
        var title = string.IsNullOrWhiteSpace(analysis.SuggestedTitle)
            ? "메일 후속 조치"
            : EvidencePolicy.Truncate(analysis.SuggestedTitle.Trim()) ?? "메일 후속 조치";

        return new LocalTaskItem(
            Guid.NewGuid(),
            title,
            analysis.DueAt,
            source.SourceHash,
            source.SourceId,
            analysis.Confidence,
            EvidencePolicy.Truncate(analysis.Reason) ?? "메일 후속 조치 분석",
            EvidencePolicy.Truncate(analysis.EvidenceSnippet),
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceDerivedDataDeleted: false,
            SourceSenderDisplay: EvidencePolicy.Truncate(source.SenderDisplay),
            SourceReceivedAt: source.ReceivedAt,
            SourceRecipientRole: source.MailboxRecipientRole,
            Kind: analysis.Kind,
            SourceConversationId: EvidencePolicy.Truncate(source.ConversationId),
            SourceRecipientDisplayNames: NormalizeRecipients(source.RecipientDisplayNames));
    }

    public LocalTaskItem MarkDone(DateTimeOffset now) => this with
    {
        Status = LocalTaskStatus.Done,
        UpdatedAt = now,
        SnoozeUntil = null
    };

    public LocalTaskItem Archive(DateTimeOffset now) => this with
    {
        Status = LocalTaskStatus.Archived,
        UpdatedAt = now,
        SnoozeUntil = null
    };

    public LocalTaskItem SnoozeUntilTime(DateTimeOffset until, DateTimeOffset now) => this with
    {
        Status = LocalTaskStatus.Snoozed,
        SnoozeUntil = until,
        UpdatedAt = now
    };

    public LocalTaskItem UpdateDetails(TaskEditRequest edit, DateTimeOffset now) => this with
    {
        Title = edit.Title,
        Kind = edit.Kind,
        DueAt = edit.DueAt,
        Status = Status == LocalTaskStatus.Snoozed ? LocalTaskStatus.Open : Status,
        SnoozeUntil = null,
        UpdatedAt = now
    };

    public LocalTaskItem DeleteSourceDerivedData(DateTimeOffset now) => this with
    {
        Title = RedactedTitle,
        Reason = RedactedReason,
        EvidenceSnippet = null,
        SourceId = null,
        SourceDerivedDataDeleted = true,
        SourceSenderDisplay = null,
        SourceReceivedAt = null,
        SourceRecipientRole = MailboxRecipientRole.Other,
        SourceConversationId = null,
        SourceRecipientDisplayNames = null,
        UpdatedAt = now
    };

    private static IReadOnlyList<string>? NormalizeRecipients(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var normalized = names
            .Select(name => string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => EvidencePolicy.Truncate(name) ?? name)
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }
}
