using MailWhere.Core.Domain;

namespace MailWhere.Core.Search;

public sealed record MailMirrorLocator(string StoreId, string EntryId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(StoreId) && !string.IsNullOrWhiteSpace(EntryId);
}

public sealed record MailMirrorMessage(
    string StoreId,
    string EntryId,
    MailSourceFolder Folder,
    DateTimeOffset LastModifiedAt,
    string Subject,
    string SenderDisplay,
    string BodyText,
    DateTimeOffset? ReceivedAt = null,
    DateTimeOffset? SentAt = null,
    string? ConversationId = null,
    IReadOnlyList<string>? RecipientDisplayNames = null)
{
    public MailMirrorLocator Locator => new(StoreId, EntryId);
}

public sealed record MailMirrorCheckpoint(string Folder, string Value);

public sealed record MailMirrorKnownState(MailMirrorLocator Locator, DateTimeOffset LastModifiedAt);

public sealed record MailMirrorBatchResult(int UpsertedCount, MailMirrorCheckpoint? Checkpoint);

public sealed record MailMirrorSearchRequest(
    string? Query = null,
    string? SenderOrRecipient = null,
    MailSourceFolder? Folder = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? ConversationId = null,
    int Limit = 20);

public sealed record MailMirrorSearchResult(
    MailMirrorLocator Locator,
    MailSourceFolder Folder,
    string Subject,
    string SenderDisplay,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? ConversationId,
    string Snippet);

public interface IMailMirrorStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task PrewarmAsync(CancellationToken cancellationToken = default);
    Task<MailMirrorBatchResult> UpsertBatchAsync(
        IReadOnlyList<MailMirrorMessage> messages,
        MailMirrorCheckpoint? checkpoint = null,
        CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(IReadOnlyList<MailMirrorLocator> locators, CancellationToken cancellationToken = default);
    Task<int> ReconcileFolderAsync(string folder, IReadOnlyList<MailMirrorLocator> currentLocators, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailMirrorSearchResult>> SearchAsync(MailMirrorSearchRequest request, CancellationToken cancellationToken = default);
    Task<string?> GetCheckpointAsync(string folder, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<MailMirrorLocator, DateTimeOffset>> GetKnownLastModifiedAsync(
        IReadOnlyList<MailMirrorLocator> locators,
        CancellationToken cancellationToken = default);
    Task RebuildFtsAsync(CancellationToken cancellationToken = default);
}
