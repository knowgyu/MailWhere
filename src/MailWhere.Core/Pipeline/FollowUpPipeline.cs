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

    public int PreferredBatchSize => _analyzer is IFollowUpBatchAnalyzer batchAnalyzer
        ? Math.Clamp(batchAnalyzer.PreferredBatchSize, 1, 8)
        : 1;

    public async Task<PipelineOutcome> ProcessAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        if (await _store.HasProcessedSourceAsync(email.SourceHash, cancellationToken).ConfigureAwait(false))
        {
            return new PipelineOutcome(PipelineOutcomeKind.Duplicate);
        }

        var analysis = await _analyzer.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false);
        return await PersistAnalysisAsync(email, analysis, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PipelineOutcome>> ProcessBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
        {
            return Array.Empty<PipelineOutcome>();
        }

        var outcomes = new PipelineOutcome[emails.Count];
        var pendingIndexes = new List<int>(emails.Count);
        var pendingEmails = new List<EmailSnapshot>(emails.Count);
        for (var i = 0; i < emails.Count; i++)
        {
            if (await _store.HasProcessedSourceAsync(emails[i].SourceHash, cancellationToken).ConfigureAwait(false))
            {
                outcomes[i] = new PipelineOutcome(PipelineOutcomeKind.Duplicate);
                continue;
            }

            pendingIndexes.Add(i);
            pendingEmails.Add(emails[i]);
        }

        if (pendingEmails.Count == 0)
        {
            return outcomes;
        }

        IReadOnlyList<FollowUpAnalysis> analyses;
        if (_analyzer is IFollowUpBatchAnalyzer batchAnalyzer && pendingEmails.Count > 1)
        {
            analyses = await batchAnalyzer.AnalyzeBatchAsync(pendingEmails, cancellationToken).ConfigureAwait(false);
            if (analyses.Count != pendingEmails.Count)
            {
                throw new InvalidOperationException("Batch analyzer returned a mismatched result count.");
            }
        }
        else
        {
            var sequential = new List<FollowUpAnalysis>(pendingEmails.Count);
            foreach (var email in pendingEmails)
            {
                sequential.Add(await _analyzer.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false));
            }

            analyses = sequential;
        }

        for (var i = 0; i < pendingEmails.Count; i++)
        {
            outcomes[pendingIndexes[i]] = await PersistAnalysisAsync(pendingEmails[i], analyses[i], cancellationToken).ConfigureAwait(false);
        }

        return outcomes;
    }

    private async Task<PipelineOutcome> PersistAnalysisAsync(EmailSnapshot email, FollowUpAnalysis analysis, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (analysis.IsTransientLlmFailureReview)
        {
            if (await _store.HasOpenLlmFailureReviewCandidateForSourceAsync(email.SourceHash, cancellationToken).ConfigureAwait(false))
            {
                return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
            }

            var transientCandidate = ReviewCandidate.FromAnalysis(email, analysis, now);
            await _store.SaveReviewCandidateAsync(transientCandidate, cancellationToken).ConfigureAwait(false);
            return new PipelineOutcome(PipelineOutcomeKind.ReviewCandidateCreated, analysis, transientCandidate.Id);
        }

        await _store.SuppressOpenLlmFailureReviewCandidatesForSourceAsync(email.SourceHash, now, "Reanalyzed", cancellationToken).ConfigureAwait(false);
        var actionSignature = FollowUpActionSignature.Create(email, analysis);
        if (actionSignature is not null
            && await _store.HasProcessedSourceAsync(actionSignature, cancellationToken).ConfigureAwait(false))
        {
            await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
            return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
        }

        switch (analysis.Disposition)
        {
            case AnalysisDisposition.AutoCreateTask:
                var task = LocalTaskItem.FromAnalysis(email, analysis, now);
                await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                if (actionSignature is not null)
                {
                    await _store.MarkSourceProcessedAsync(actionSignature, cancellationToken).ConfigureAwait(false);
                }

                return new PipelineOutcome(PipelineOutcomeKind.TaskCreated, analysis, task.Id);

            case AnalysisDisposition.Review:
                var candidate = ReviewCandidate.FromAnalysis(email, analysis, now);
                await _store.SaveReviewCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                if (actionSignature is not null)
                {
                    await _store.MarkSourceProcessedAsync(actionSignature, cancellationToken).ConfigureAwait(false);
                }

                return new PipelineOutcome(PipelineOutcomeKind.ReviewCandidateCreated, analysis, candidate.Id);

            default:
                await _store.MarkSourceProcessedAsync(email.SourceHash, cancellationToken).ConfigureAwait(false);
                return new PipelineOutcome(PipelineOutcomeKind.Ignored, analysis);
        }
    }
}
