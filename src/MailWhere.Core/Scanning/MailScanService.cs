using MailWhere.Core.Analysis;
using MailWhere.Core.Domain;
using MailWhere.Core.Mail;
using MailWhere.Core.Pipeline;

namespace MailWhere.Core.Scanning;

public sealed record MailScanRequest(
    int MaxItems,
    bool IncludeBody,
    DateTimeOffset Since,
    int LlmInitialConcurrency = 2,
    int LlmMaxConcurrency = 4)
{
    public static MailScanRequest RecentMonth(DateTimeOffset now, int maxItems = 0, bool includeBody = true) =>
        new(maxItems, includeBody, now.AddDays(-30));

    public int EffectiveLlmConcurrency
    {
        get
        {
            var initial = Math.Clamp(LlmInitialConcurrency, 1, 4);
            var max = Math.Clamp(LlmMaxConcurrency, 1, 4);
            return Math.Min(initial, max);
        }
    }
}

public sealed record MailScanProgress(string Phase, int Processed, int? Total, string Message);

public sealed record MailScanSummary(
    int ReadCount,
    int TaskCreatedCount,
    int ReviewCandidateCount,
    int IgnoredCount,
    int DuplicateCount,
    int SkippedCount,
    IReadOnlyList<MailReadWarning> Warnings);

public sealed class MailActionScanner
{
    private readonly IEmailSource _emailSource;
    private readonly FollowUpPipeline _pipeline;

    public MailActionScanner(IEmailSource emailSource, FollowUpPipeline pipeline)
    {
        _emailSource = emailSource;
        _pipeline = pipeline;
    }

    public Task<MailScanSummary> ScanAsync(MailScanRequest request, CancellationToken cancellationToken = default) =>
        ScanAsync(request, progress: null, cancellationToken);

    public async Task<MailScanSummary> ScanAsync(
        MailScanRequest request,
        IProgress<MailScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new MailScanProgress("reading", 0, null, "Outlook에서 최근 1개월 메일을 읽는 중입니다…"));
        var result = await _emailSource.ReadAsync(new MailReadRequest(request.MaxItems, request.IncludeBody, request.Since), cancellationToken).ConfigureAwait(false);
        progress?.Report(new MailScanProgress("analyzing", 0, result.Messages.Count, $"메일 {result.Messages.Count}건을 분석하는 중입니다…"));
        var created = 0;
        var review = 0;
        var ignored = 0;
        var duplicate = 0;
        var processed = 0;

        var maxBatchSize = Math.Max(1, _pipeline.PreferredBatchSize);
        var preparedBatches = new List<PreparedPipelineBatch>();
        var reservedSourceHashes = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < result.Messages.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchSize = SelectAdaptiveBatchSize(result.Messages, start, maxBatchSize);
            var batch = result.Messages.Skip(start).Take(batchSize).ToArray();
            var nextCount = Math.Min(result.Messages.Count, start + batch.Length);
            var message = batch.Length == 1
                ? $"메일 분석 중 {start + 1}/{result.Messages.Count} · 오래 걸리면 중지할 수 있습니다"
                : $"메일 분석 중 {start + 1}-{nextCount}/{result.Messages.Count} · 오래 걸리면 중지할 수 있습니다";
            progress?.Report(new MailScanProgress("analyzing", processed, result.Messages.Count, message));

            preparedBatches.Add(await _pipeline.PrepareBatchAsync(batch, reservedSourceHashes, cancellationToken).ConfigureAwait(false));
            start += batch.Length;
        }

        var analysesByBatch = await AnalyzePreparedBatchesAsync(preparedBatches, request, maxBatchSize, progress, result.Messages.Count, cancellationToken).ConfigureAwait(false);

        for (var batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcomes = await _pipeline.PersistPreparedBatchAsync(preparedBatches[batchIndex], analysesByBatch[batchIndex], cancellationToken).ConfigureAwait(false);
            foreach (var outcome in outcomes)
            {
                processed++;
                switch (outcome.Kind)
                {
                    case PipelineOutcomeKind.TaskCreated:
                        created++;
                        break;
                    case PipelineOutcomeKind.ReviewCandidateCreated:
                        review++;
                        break;
                    case PipelineOutcomeKind.Ignored:
                        ignored++;
                        break;
                    case PipelineOutcomeKind.Duplicate:
                        duplicate++;
                        break;
                }

                progress?.Report(new MailScanProgress("analyzing", processed, result.Messages.Count, $"메일 분석 중 {processed}/{result.Messages.Count}"));
            }
        }

        progress?.Report(new MailScanProgress("completed", processed, result.Messages.Count, "메일 확인이 완료되었습니다."));
        return new MailScanSummary(result.Messages.Count, created, review, ignored, duplicate, result.SkippedCount, result.Warnings);
    }

    private async Task<IReadOnlyList<FollowUpAnalysis>[]> AnalyzePreparedBatchesAsync(
        IReadOnlyList<PreparedPipelineBatch> preparedBatches,
        MailScanRequest request,
        int maxBatchSize,
        IProgress<MailScanProgress>? progress,
        int totalCount,
        CancellationToken cancellationToken)
    {
        var analysesByBatch = new IReadOnlyList<FollowUpAnalysis>[preparedBatches.Count];
        if (preparedBatches.Count == 0)
        {
            return analysesByBatch;
        }

        var batchStartNumbers = BuildBatchStartNumbers(preparedBatches);
        var effectiveConcurrency = maxBatchSize > 1 ? request.EffectiveLlmConcurrency : 1;
        using var gate = new SemaphoreSlim(effectiveConcurrency, effectiveConcurrency);
        var tasks = preparedBatches.Select(async (batch, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch.PendingEmails.Count == 0)
            {
                analysesByBatch[index] = Array.Empty<FollowUpAnalysis>();
                return;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = batchStartNumbers[index];
                var last = first + batch.Emails.Count - 1;
                var range = first == last ? first.ToString() : $"{first}-{last}";
                progress?.Report(new MailScanProgress("analyzing", 0, totalCount, $"메일 분석 요청 중 {range}/{totalCount}"));
                analysesByBatch[index] = await _pipeline.AnalyzePreparedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return analysesByBatch;
    }

    private static int[] BuildBatchStartNumbers(IReadOnlyList<PreparedPipelineBatch> preparedBatches)
    {
        var starts = new int[preparedBatches.Count];
        var nextStart = 1;
        for (var index = 0; index < preparedBatches.Count; index++)
        {
            starts[index] = nextStart;
            nextStart += preparedBatches[index].Emails.Count;
        }

        return starts;
    }

    private static int SelectAdaptiveBatchSize(IReadOnlyList<EmailSnapshot> messages, int start, int maxBatchSize)
    {
        if (maxBatchSize <= 1 || start >= messages.Count)
        {
            return 1;
        }

        var firstLength = EstimateInputLength(messages[start]);
        var ceiling = firstLength switch
        {
            >= 8000 => 1,
            >= 5000 => Math.Min(2, maxBatchSize),
            >= 2500 => Math.Min(4, maxBatchSize),
            >= 1200 => Math.Min(8, maxBatchSize),
            _ => maxBatchSize
        };

        const int BatchLengthBudget = 12000;
        var selected = 0;
        var totalLength = 0;
        while (selected < ceiling && start + selected < messages.Count)
        {
            var nextLength = EstimateInputLength(messages[start + selected]);
            if (selected > 0 && totalLength + nextLength > BatchLengthBudget)
            {
                break;
            }

            totalLength += nextLength;
            selected++;
        }

        return Math.Max(1, selected);
    }

    private static int EstimateInputLength(EmailSnapshot message) =>
        (message.Subject?.Length ?? 0)
        + (message.SenderDisplay?.Length ?? 0)
        + (message.Body?.Length ?? 0)
        + (message.RecipientDisplayNames?.Sum(name => name.Length) ?? 0);
}
