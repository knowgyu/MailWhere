using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

public interface IFollowUpAnalyzer
{
    Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default);
}

public interface IFollowUpBatchAnalyzer : IFollowUpAnalyzer
{
    int PreferredBatchSize { get; }

    Task<IReadOnlyList<FollowUpAnalysis>> AnalyzeBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default);
}
