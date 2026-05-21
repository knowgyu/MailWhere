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

public sealed record PreparedPipelineBatch(
    IReadOnlyList<EmailSnapshot> Emails,
    IReadOnlyList<PipelineOutcome?> Outcomes,
    IReadOnlyList<int> PendingIndexes,
    IReadOnlyList<EmailSnapshot> PendingEmails);

public sealed record MailFastFilterResult(IReadOnlyList<EmailSnapshot> PendingEmails, int DuplicateCount);

public sealed class FollowUpPipeline
{
    private readonly IFollowUpAnalyzer _analyzer;
    private readonly IFollowUpStore _store;
    private readonly IWaitingClosureJudge _waitingClosureJudge;
    private readonly TimeProvider _timeProvider;

    public FollowUpPipeline(IFollowUpAnalyzer analyzer, IFollowUpStore store, TimeProvider? timeProvider = null, IWaitingClosureJudge? waitingClosureJudge = null)
    {
        _analyzer = analyzer;
        _store = store;
        _waitingClosureJudge = waitingClosureJudge ?? new RuleBasedWaitingClosureJudge();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int PreferredBatchSize => _analyzer is IFollowUpBatchAnalyzer batchAnalyzer
        ? Math.Clamp(batchAnalyzer.PreferredBatchSize, 1, 16)
        : 1;

    public async Task<PipelineOutcome> ProcessAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        var batch = await PrepareBatchAsync(new[] { email }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var analyses = await AnalyzePreparedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        var outcomes = await PersistPreparedBatchAsync(batch, analyses, cancellationToken).ConfigureAwait(false);
        return outcomes[0];
    }

    public async Task<IReadOnlyList<PipelineOutcome>> ProcessBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
        {
            return Array.Empty<PipelineOutcome>();
        }

        var reservedSourceHashes = new HashSet<string>(StringComparer.Ordinal);
        var prepared = await PrepareBatchAsync(emails, reservedSourceHashes, cancellationToken).ConfigureAwait(false);
        var analyses = await AnalyzePreparedBatchAsync(prepared, cancellationToken).ConfigureAwait(false);
        return await PersistPreparedBatchAsync(prepared, analyses, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreparedPipelineBatch> PrepareBatchAsync(
        IReadOnlyList<EmailSnapshot> emails,
        ISet<string>? reservedSourceHashes = null,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new PipelineOutcome[emails.Count];
        var pendingIndexes = new List<int>(emails.Count);
        var pendingEmails = new List<EmailSnapshot>(emails.Count);
        var closureCandidateEmails = new List<EmailSnapshot>(emails.Count);
        foreach (var email in emails)
        {
            await _store.RecordReplyObservationAsync(email, cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < emails.Count; i++)
        {
            if (await _store.HasProcessedSourceAsync(emails[i].SourceHash, cancellationToken).ConfigureAwait(false))
            {
                outcomes[i] = new PipelineOutcome(PipelineOutcomeKind.Duplicate);
                continue;
            }

            if (reservedSourceHashes is not null && !reservedSourceHashes.Add(emails[i].SourceHash))
            {
                outcomes[i] = new PipelineOutcome(PipelineOutcomeKind.Duplicate);
                continue;
            }

            closureCandidateEmails.Add(emails[i]);
            pendingIndexes.Add(i);
            pendingEmails.Add(emails[i]);
        }

        await CreateWaitingClosureSuggestionsAsync(closureCandidateEmails, cancellationToken).ConfigureAwait(false);

        return new PreparedPipelineBatch(
            emails.ToArray(),
            outcomes,
            pendingIndexes.ToArray(),
            pendingEmails.ToArray());
    }

    public async Task<MailFastFilterResult> FastFilterAsync(
        IReadOnlyList<EmailSnapshot> emails,
        CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
        {
            return new MailFastFilterResult(Array.Empty<EmailSnapshot>(), DuplicateCount: 0);
        }

        var pending = new List<EmailSnapshot>(emails.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var email in emails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _store.HasProcessedSourceAsync(email.SourceHash, cancellationToken).ConfigureAwait(false)
                || !seen.Add(email.SourceHash))
            {
                duplicates++;
                continue;
            }

            pending.Add(email);
        }

        return new MailFastFilterResult(pending.ToArray(), duplicates);
    }

    public Task<int> CreateWaitingClosureSuggestionsAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default) =>
        new WaitingClosureSuggestionService(_store, _waitingClosureJudge, _timeProvider)
            .CreateSuggestionsAsync(emails, cancellationToken);

    public async Task<IReadOnlyList<FollowUpAnalysis>> AnalyzePreparedBatchAsync(
        PreparedPipelineBatch prepared,
        CancellationToken cancellationToken = default)
    {
        if (prepared.PendingEmails.Count == 0)
        {
            return Array.Empty<FollowUpAnalysis>();
        }

        if (_analyzer is IFollowUpBatchAnalyzer batchAnalyzer && prepared.PendingEmails.Count > 1)
        {
            var analyses = await batchAnalyzer.AnalyzeBatchAsync(prepared.PendingEmails, cancellationToken).ConfigureAwait(false);
            if (analyses.Count != prepared.PendingEmails.Count)
            {
                throw new InvalidOperationException("Batch analyzer returned a mismatched result count.");
            }

            return analyses;
        }

        var sequential = new List<FollowUpAnalysis>(prepared.PendingEmails.Count);
        foreach (var email in prepared.PendingEmails)
        {
            sequential.Add(await _analyzer.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false));
        }

        return sequential;
    }

    public async Task<IReadOnlyList<PipelineOutcome>> PersistPreparedBatchAsync(
        PreparedPipelineBatch prepared,
        IReadOnlyList<FollowUpAnalysis> analyses,
        CancellationToken cancellationToken = default)
    {
        if (analyses.Count != prepared.PendingEmails.Count)
        {
            throw new InvalidOperationException("Prepared analysis count does not match pending emails.");
        }

        var outcomes = prepared.Outcomes.ToArray();
        for (var i = 0; i < prepared.PendingEmails.Count; i++)
        {
            outcomes[prepared.PendingIndexes[i]] = await PersistAnalysisAsync(prepared.PendingEmails[i], analyses[i], cancellationToken).ConfigureAwait(false);
        }

        if (outcomes.Any(outcome => outcome is null))
        {
            throw new InvalidOperationException("Prepared pipeline batch was not fully resolved.");
        }

        return outcomes.Select(outcome => outcome!).ToArray();
    }

    private async Task<PipelineOutcome> PersistAnalysisAsync(EmailSnapshot email, FollowUpAnalysis analysis, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (await _store.HasProcessedSourceAsync(email.SourceHash, cancellationToken).ConfigureAwait(false))
        {
            return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
        }

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
                if (!await _store.TrySaveTaskWithProcessedSourcesAsync(task, actionSignature, cancellationToken).ConfigureAwait(false))
                {
                    return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
                }

                return new PipelineOutcome(PipelineOutcomeKind.TaskCreated, analysis, task.Id);

            case AnalysisDisposition.Review:
                var candidate = ReviewCandidate.FromAnalysis(email, analysis, now);
                if (!await _store.TrySaveReviewCandidateWithProcessedSourcesAsync(candidate, actionSignature, cancellationToken).ConfigureAwait(false))
                {
                    return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
                }

                return new PipelineOutcome(PipelineOutcomeKind.ReviewCandidateCreated, analysis, candidate.Id);

            default:
                if (!await _store.TryMarkProcessedSourcesAsync(email.SourceHash, actionSignature, cancellationToken).ConfigureAwait(false))
                {
                    return new PipelineOutcome(PipelineOutcomeKind.Duplicate, analysis);
                }

                return new PipelineOutcome(PipelineOutcomeKind.Ignored, analysis);
        }
    }
}
