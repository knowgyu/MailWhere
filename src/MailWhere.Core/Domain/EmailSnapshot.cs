namespace MailWhere.Core.Domain;

public enum MailboxRecipientRole
{
    Direct,
    Cc,
    Bcc,
    Other
}

public sealed record EmailSnapshot(
    string SourceId,
    DateTimeOffset ReceivedAt,
    string SenderDisplay,
    string Subject,
    string? Body,
    string? ConversationId = null,
    string? MailboxOwnerDisplayName = null,
    IReadOnlyList<string>? RecipientDisplayNames = null,
    MailboxRecipientRole MailboxRecipientRole = MailboxRecipientRole.Direct)
{
    public string SourceHash => StableHash.Create(SourceId);
}
