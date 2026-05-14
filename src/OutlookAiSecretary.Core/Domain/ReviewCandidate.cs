namespace OutlookAiSecretary.Core.Domain;

public sealed record ReviewCandidate(
    Guid Id,
    string SourceIdHash,
    FollowUpAnalysis Analysis,
    DateTimeOffset CreatedAt,
    bool Suppressed = false)
{
    public static ReviewCandidate FromAnalysis(EmailSnapshot source, FollowUpAnalysis analysis, DateTimeOffset now) =>
        new(Guid.NewGuid(), source.SourceHash, analysis with { SuggestedTitle = EvidencePolicy.Truncate(analysis.SuggestedTitle) ?? string.Empty, Reason = EvidencePolicy.Truncate(analysis.Reason) ?? "Review candidate", EvidenceSnippet = EvidencePolicy.Truncate(analysis.EvidenceSnippet) }, now);
}
