using MailWhere.Core.Domain;
using MailWhere.Core.Mail;
using MailWhere.Core.Pipeline;

namespace MailWhere.Core.Scanning;

public sealed record MailScanRequest(int MaxItems, bool IncludeBody, DateTimeOffset Since)
{
    public static MailScanRequest RecentMonth(DateTimeOffset now, int maxItems = 200, bool includeBody = true) =>
        new(maxItems, includeBody, now.AddDays(-30));
}

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

    public async Task<MailScanSummary> ScanAsync(MailScanRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _emailSource.ReadAsync(new MailReadRequest(request.MaxItems, request.IncludeBody, request.Since), cancellationToken).ConfigureAwait(false);
        var created = 0;
        var review = 0;
        var ignored = 0;
        var duplicate = 0;

        foreach (var message in result.Messages)
        {
            var outcome = await _pipeline.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
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
        }

        return new MailScanSummary(result.Messages.Count, created, review, ignored, duplicate, result.SkippedCount, result.Warnings);
    }
}
