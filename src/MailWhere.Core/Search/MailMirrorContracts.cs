using MailWhere.Core.Domain;

namespace MailWhere.Core.Search;

public static class MailMirrorOpenSourceToken
{
    private const string Prefix = "mailwhere-open-source-token-v1";

    public static string Create(string storeId, string entryId, int collisionNonce = 0) =>
        MailMirrorText.Hash($"{Prefix}\n{MailMirrorText.Normalize(storeId)}\n{MailMirrorText.Normalize(entryId)}\n{collisionNonce}");

    public static bool IsValid(string? token) =>
        token is { Length: 64 } && token.All(static ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
}

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
    string? OpenSourceToken,
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
    Task<MailMirrorLocator?> ResolveOpenSourceTokenAsync(string openSourceToken, CancellationToken cancellationToken = default);
    Task<string?> GetCheckpointAsync(string folder, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<MailMirrorLocator, DateTimeOffset>> GetKnownLastModifiedAsync(
        IReadOnlyList<MailMirrorLocator> locators,
        CancellationToken cancellationToken = default);
    Task RebuildFtsAsync(CancellationToken cancellationToken = default);
}
