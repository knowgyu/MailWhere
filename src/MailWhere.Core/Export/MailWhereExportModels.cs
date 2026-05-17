using MailWhere.Core.Domain;

namespace MailWhere.Core.Export;

public sealed record MailWhereExportSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<MailWhereExportTask> OpenTasks,
    IReadOnlyList<MailWhereExportTask> ArchivedTasks,
    IReadOnlyList<MailWhereExportReviewCandidate> ReviewItems,
    IReadOnlyList<MailWhereExportReplyProgress> ReplyProgress);

public sealed record MailWhereExportTask(
    Guid Id,
    string Title,
    string Status,
    string Kind,
    DateTimeOffset? DueAt,
    string? SenderDisplay,
    DateTimeOffset? SourceReceivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanOpenSource,
    bool SourceDerivedDataDeleted)
{
    public static MailWhereExportTask FromTask(LocalTaskItem task) => new(
        task.Id,
        FollowUpPresentation.ActionTitle(task.Title),
        task.Status.ToString(),
        task.Kind.ToString(),
        task.DueAt,
        SafeText(task.SourceSenderDisplay),
        task.SourceReceivedAt,
        task.CreatedAt,
        task.UpdatedAt,
        !task.SourceDerivedDataDeleted && !string.IsNullOrWhiteSpace(task.SourceId),
        task.SourceDerivedDataDeleted);

    private static string? SafeText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : EvidencePolicy.Truncate(value.Trim());
}

public sealed record MailWhereExportReviewCandidate(
    Guid Id,
    string Title,
    string Kind,
    DateTimeOffset? DueAt,
    double Confidence,
    string? SenderDisplay,
    DateTimeOffset? SourceReceivedAt,
    DateTimeOffset CreatedAt)
{
    public static MailWhereExportReviewCandidate FromCandidate(ReviewCandidate candidate) => new(
        candidate.Id,
        FollowUpPresentation.ActionTitle(candidate.Analysis.SuggestedTitle),
        candidate.Analysis.Kind.ToString(),
        candidate.Analysis.DueAt,
        Math.Clamp(candidate.Analysis.Confidence, 0, 1),
        SafeText(candidate.SourceSenderDisplay),
        candidate.SourceReceivedAt,
        candidate.CreatedAt);

    private static string? SafeText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : EvidencePolicy.Truncate(value.Trim());
}

public sealed record MailWhereExportReplyProgress(
    Guid TaskId,
    string Title,
    int ExpectedCount,
    int ReceivedCount,
    string SummaryText,
    IReadOnlyList<MailWhereExportReplyParticipant> Participants)
{
    public static MailWhereExportReplyProgress FromProgress(ReplyProgressItem progress) => new(
        progress.TaskId,
        progress.Title,
        progress.ExpectedCount,
        progress.ReceivedCount,
        progress.SummaryText,
        progress.Participants
            .Select((participant, index) => MailWhereExportReplyParticipant.FromParticipant(index + 1, participant))
            .ToArray());
}

public sealed record MailWhereExportReplyParticipant(
    int Ordinal,
    bool HasReplied,
    DateTimeOffset? RepliedAt)
{
    public static MailWhereExportReplyParticipant FromParticipant(int ordinal, ReplyParticipantProgress participant) => new(
        ordinal,
        participant.HasReplied,
        participant.RepliedAt);
}
