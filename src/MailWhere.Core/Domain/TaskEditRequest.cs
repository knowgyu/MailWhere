namespace MailWhere.Core.Domain;

public sealed record TaskEditRequest(string Title, FollowUpKind Kind, DateTimeOffset? DueAt)
{
    public static TaskEditRequest Create(string? title, FollowUpKind kind, DateTimeOffset? dueAt)
    {
        var normalizedTitle = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            throw new ArgumentException("Task title is required.", nameof(title));
        }

        return new TaskEditRequest(normalizedTitle, NormalizeKind(kind), dueAt);
    }

    public static string NormalizeTitle(string? title) =>
        EvidencePolicy.Truncate(title) ?? string.Empty;

    public static FollowUpKind NormalizeKind(FollowUpKind kind) => kind switch
    {
        FollowUpKind.WaitingForReply => FollowUpKind.WaitingForReply,
        FollowUpKind.Meeting or FollowUpKind.CalendarEvent => FollowUpKind.Meeting,
        FollowUpKind.None => FollowUpKind.ActionRequested,
        _ => FollowUpKind.ActionRequested
    };
}
