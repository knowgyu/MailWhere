using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Mail;

public sealed record MailReadWarning(string Code, CapabilitySeverity Severity, string SanitizedErrorClass);

public sealed record MailReadRequest(
    int MaxItems,
    bool IncludeBody,
    DateTimeOffset? Since = null,
    DateTimeOffset? InboxSince = null,
    DateTimeOffset? SentSince = null)
{
    public DateTimeOffset? SinceFor(MailSourceFolder folder) => folder switch
    {
        MailSourceFolder.Sent => SentSince ?? Since,
        MailSourceFolder.Inbox => InboxSince ?? Since,
        _ => Since
    };
}

public sealed record EmailReadResult(IReadOnlyList<EmailSnapshot> Messages, IReadOnlyList<MailReadWarning> Warnings, int SkippedCount)
{
    public static EmailReadResult Empty(params MailReadWarning[] warnings) => new(Array.Empty<EmailSnapshot>(), warnings, 0);
}

public interface IEmailSource
{
    Task<EmailReadResult> ReadRecentAsync(int maxItems, bool includeBody, CancellationToken cancellationToken = default) =>
        ReadAsync(new MailReadRequest(maxItems, includeBody), cancellationToken);

    Task<EmailReadResult> ReadAsync(MailReadRequest request, CancellationToken cancellationToken = default);
}

public interface IEmailHydratingSource : IEmailSource
{
    Task<EmailSnapshot?> TryReadBySourceIdAsync(string? sourceId, CancellationToken cancellationToken = default);
}
