using MailWhere.Core.Domain;

namespace MailWhere.Core.Storage;

public interface IFollowUpStore
{
    Task<bool> HasProcessedSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default);
    Task SaveTaskAsync(LocalTaskItem task, CancellationToken cancellationToken = default);
    Task SaveReviewCandidateAsync(ReviewCandidate candidate, CancellationToken cancellationToken = default);
    Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewCandidate>> ListReviewCandidatesAsync(CancellationToken cancellationToken = default);
    Task<ReviewCandidate?> GetReviewCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);
    Task<LocalTaskItem?> ResolveReviewCandidateAsTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> ResolveReviewCandidateAsNotTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task DeleteSourceDerivedDataAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task DeleteSourceDerivedDataForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default);
}

public interface IAppStateStore
{
    Task<string?> GetAppStateAsync(string key, CancellationToken cancellationToken = default);
    Task SetAppStateAsync(string key, string value, CancellationToken cancellationToken = default);
}
