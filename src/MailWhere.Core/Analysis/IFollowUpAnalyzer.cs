using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

public interface IFollowUpAnalyzer
{
    Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default);
}
