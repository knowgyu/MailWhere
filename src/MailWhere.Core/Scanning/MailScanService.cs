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

        foreach (var message in result.Messages)
        {
            var outcome = await _pipeline.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
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

        progress?.Report(new MailScanProgress("completed", processed, result.Messages.Count, "최근 1개월 스캔이 완료되었습니다."));
        return new MailScanSummary(result.Messages.Count, created, review, ignored, duplicate, result.SkippedCount, result.Warnings);
    }
}
