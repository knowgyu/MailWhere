using OutlookAiSecretary.Core.Domain;

namespace OutlookAiSecretary.Core.Storage;

public interface IFollowUpStore
{
    Task<bool> HasProcessedSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default);
    Task SaveTaskAsync(LocalTaskItem task, CancellationToken cancellationToken = default);
    Task SaveReviewCandidateAsync(ReviewCandidate candidate, CancellationToken cancellationToken = default);
    Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewCandidate>> ListReviewCandidatesAsync(CancellationToken cancellationToken = default);
    Task MarkNotATaskAsync(string sourceIdHash, CancellationToken cancellationToken = default);
    Task DeleteSourceDerivedDataAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task DeleteSourceDerivedDataForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default);
}
