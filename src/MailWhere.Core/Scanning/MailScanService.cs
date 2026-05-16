using MailWhere.Core.Domain;
using MailWhere.Core.Mail;
using MailWhere.Core.Pipeline;

namespace MailWhere.Core.Scanning;

public sealed record MailScanRequest(int MaxItems, bool IncludeBody, DateTimeOffset Since)
{
    public static MailScanRequest RecentMonth(DateTimeOffset now, int maxItems = 0, bool includeBody = true) =>
        new(maxItems, includeBody, now.AddDays(-30));
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

        var batchSize = Math.Max(1, _pipeline.PreferredBatchSize);
        for (var start = 0; start < result.Messages.Count; start += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = result.Messages.Skip(start).Take(batchSize).ToArray();
            var nextCount = Math.Min(result.Messages.Count, start + batch.Length);
            var message = batch.Length == 1
                ? $"메일 분석 중 {start + 1}/{result.Messages.Count} · 오래 걸리면 중지할 수 있습니다"
                : $"메일 분석 중 {start + 1}-{nextCount}/{result.Messages.Count} · 오래 걸리면 중지할 수 있습니다";
            progress?.Report(new MailScanProgress("analyzing", processed, result.Messages.Count, message));

            var outcomes = await _pipeline.ProcessBatchAsync(batch, cancellationToken).ConfigureAwait(false);
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
}
