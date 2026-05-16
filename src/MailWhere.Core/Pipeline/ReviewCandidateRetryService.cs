using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Pipeline;

public sealed record ReviewCandidateRetrySummary(
    int EligibleCount,
    int RetriedCount,
    int TaskCreatedCount,
    int ReviewCandidateCreatedCount,
    int IgnoredCount,
    int DuplicateCount,
    int MissingSourceCount,
    int SourceLookupFailureCount)
{
    public int ChangedCount => TaskCreatedCount + ReviewCandidateCreatedCount + IgnoredCount;
}

public sealed class ReviewCandidateRetryService
{
    private readonly IFollowUpStore _store;
    private readonly FollowUpPipeline _pipeline;
    private readonly Func<ReviewCandidate, CancellationToken, Task<EmailSnapshot?>> _sourceResolver;

    public ReviewCandidateRetryService(
        IFollowUpStore store,
        FollowUpPipeline pipeline,
        Func<ReviewCandidate, CancellationToken, Task<EmailSnapshot?>> sourceResolver)
    {
        _store = store;
        _pipeline = pipeline;
        _sourceResolver = sourceResolver;
    }

    public async Task<ReviewCandidateRetrySummary> RetryTransientLlmFailuresAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _store.ListReviewCandidatesAsync(cancellationToken).ConfigureAwait(false);
        var eligible = candidates
            .Where(candidate => candidate.Analysis.IsTransientLlmFailureReview && !candidate.Suppressed)
            .ToArray();

        var retried = 0;
        var taskCreated = 0;
        var reviewCreated = 0;
        var ignored = 0;
        var duplicate = 0;
        var missingSource = 0;
        var sourceLookupFailure = 0;

        foreach (var candidate in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmailSnapshot? source;
            try
            {
                source = await _sourceResolver(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                sourceLookupFailure++;
                continue;
            }

            if (source is null)
            {
                missingSource++;
                continue;
            }

            retried++;
            var outcome = await _pipeline.ProcessAsync(source, cancellationToken).ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case PipelineOutcomeKind.TaskCreated:
                    taskCreated++;
                    break;
                case PipelineOutcomeKind.ReviewCandidateCreated:
                    reviewCreated++;
                    break;
                case PipelineOutcomeKind.Ignored:
                    ignored++;
                    break;
                case PipelineOutcomeKind.Duplicate:
                    duplicate++;
                    break;
            }
        }

        return new ReviewCandidateRetrySummary(
            eligible.Length,
            retried,
            taskCreated,
            reviewCreated,
            ignored,
            duplicate,
            missingSource,
            sourceLookupFailure);
    }
}
