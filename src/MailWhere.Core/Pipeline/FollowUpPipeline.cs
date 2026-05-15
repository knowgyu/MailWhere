using MailWhere.Core.Analysis;
using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Pipeline;

public enum PipelineOutcomeKind
{
    Ignored,
    Duplicate,
    TaskCreated,
    ReviewCandidateCreated
}

public sealed record PipelineOutcome(PipelineOutcomeKind Kind, FollowUpAnalysis? Analysis = null, Guid? ItemId = null);

public sealed class FollowUpPipeline
{
    private readonly IFollowUpAnalyzer _analyzer;
    private readonly IFollowUpStore _store;
    private readonly TimeProvider _timeProvider;

    public FollowUpPipeline(IFollowUpAnalyzer analyzer, IFollowUpStore store, TimeProvider? timeProvider = null)
    {
        _analyzer = analyzer;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PipelineOutcome> ProcessAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        if (await _store.HasProcessedSourceAsync(email.SourceHash, cancellationToken).ConfigureAwait(false))
        {
            return new PipelineOutcome(PipelineOutcomeKind.Duplicate);
        }

        var analysis = await _analyzer.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        switch (analysis.Disposition)
        {
            case AnalysisDisposition.AutoCreateTask:
                var task = LocalTaskItem.FromAnalysis(email, analysis, now);
                await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                return new PipelineOutcome(PipelineOutcomeKind.TaskCreated, analysis, task.Id);

            case AnalysisDisposition.Review:
                var candidate = ReviewCandidate.FromAnalysis(email, analysis, now);
                await _store.SaveReviewCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                return new PipelineOutcome(PipelineOutcomeKind.ReviewCandidateCreated, analysis, candidate.Id);

            default:
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                return new PipelineOutcome(PipelineOutcomeKind.Ignored, analysis);
        }
    }
}
