namespace MailWhere.Core.Domain;

public sealed record EmailSnapshot(
    string SourceId,
    DateTimeOffset ReceivedAt,
    string SenderDisplay,
    string Subject,
    string? Body,
    string? ConversationId = null,
    string? MailboxOwnerDisplayName = null,
    IReadOnlyList<string>? RecipientDisplayNames = null)
{
    public string SourceHash => StableHash.Create(SourceId);
}
