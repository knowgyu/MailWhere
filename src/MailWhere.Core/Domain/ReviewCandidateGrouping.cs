using System.Text.RegularExpressions;

namespace MailWhere.Core.Domain;

public static partial class ReviewCandidateGrouping
{
    public static IReadOnlyList<IReadOnlyList<ReviewCandidate>> Group(IEnumerable<ReviewCandidate> candidates)
    {
        var groups = new List<List<ReviewCandidate>>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = Key(candidate);
            if (!indexes.TryGetValue(key, out var index))
            {
                indexes[key] = groups.Count;
                groups.Add([]);
                index = groups.Count - 1;
            }

            groups[index].Add(candidate);
        }

        return groups;
    }

    private static string Key(ReviewCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.SourceSenderDisplay))
        {
            return candidate.Id.ToString("N");
        }

        var sender = NonSemanticCharsRegex().Replace(candidate.SourceSenderDisplay.ToLowerInvariant(), string.Empty);
        var rawTitle = FollowUpPresentation.ActionTitle(candidate.Analysis.SuggestedTitle).ToLowerInvariant();
        var title = NonSemanticCharsRegex().Replace(DynamicNumberRegex().Replace(rawTitle, "#"), string.Empty);
        if (sender.Length == 0 || !rawTitle.Any(char.IsLetter))
        {
            return candidate.Id.ToString("N");
        }

        return $"{sender}|{title}";
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex DynamicNumberRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}#]+")]
    private static partial Regex NonSemanticCharsRegex();
}
