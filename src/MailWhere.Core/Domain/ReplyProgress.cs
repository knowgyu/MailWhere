namespace MailWhere.Core.Domain;

public sealed record ReplyParticipantProgress(
    string DisplayName,
    bool HasReplied,
    DateTimeOffset? RepliedAt);

public sealed record ReplyProgressItem(
    Guid TaskId,
    string Title,
    string ConversationId,
    int ExpectedCount,
    int ReceivedCount,
    IReadOnlyList<ReplyParticipantProgress> Participants)
{
    public string SummaryText => ExpectedCount <= 0
        ? "회신 현황 없음"
        : $"{ReceivedCount}/{ExpectedCount}명 회신";
}

public static class ReplyProgressMatcher
{
    public static string NormalizeParticipantKey(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var compact = string.Join(' ', displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return compact.ToUpperInvariant();
    }

    public static ReplyProgressItem? Build(LocalTaskItem task, IEnumerable<ReplyReceipt> receipts)
    {
        if (task.Kind != FollowUpKind.WaitingForReply
            || string.IsNullOrWhiteSpace(task.SourceConversationId)
            || task.SourceRecipientDisplayNames is null)
        {
            return null;
        }

        var expected = task.SourceRecipientDisplayNames
            .Select(name => new { DisplayName = name, Key = NormalizeParticipantKey(name) })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (expected.Length < 2)
        {
            return null;
        }

        var receiptMap = receipts
            .Where(receipt => string.Equals(receipt.ConversationId, task.SourceConversationId, StringComparison.Ordinal))
            .GroupBy(receipt => NormalizeParticipantKey(receipt.ParticipantDisplay), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(receipt => receipt.ReceivedAt), StringComparer.Ordinal);

        var participants = expected
            .Select(item =>
            {
                var hasReplied = receiptMap.TryGetValue(item.Key, out var repliedAt);
                return new ReplyParticipantProgress(item.DisplayName, hasReplied, hasReplied ? repliedAt : null);
            })
            .ToArray();

        return new ReplyProgressItem(
            task.Id,
            FollowUpPresentation.ActionTitle(task.Title),
            task.SourceConversationId!,
            participants.Length,
            participants.Count(participant => participant.HasReplied),
            participants);
    }
}

public sealed record ReplyReceipt(
    string ConversationId,
    string ParticipantDisplay,
    DateTimeOffset ReceivedAt,
    string? SourceIdHash = null);
