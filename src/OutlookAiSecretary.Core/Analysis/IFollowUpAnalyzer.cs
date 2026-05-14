using OutlookAiSecretary.Core.Domain;

namespace OutlookAiSecretary.Core.Analysis;

public interface IFollowUpAnalyzer
{
    Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default);
}
