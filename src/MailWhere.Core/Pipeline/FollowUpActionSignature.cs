using System.Text.RegularExpressions;
using MailWhere.Core.Analysis;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Pipeline;

public static partial class FollowUpActionSignature
{
    public static string? Create(EmailSnapshot email, FollowUpAnalysis analysis)
    {
        if (analysis.Disposition != AnalysisDisposition.AutoCreateTask)
        {
            return null;
        }

        var context = MailBodyContextBuilder.Build(email);
        var threadKey = !string.IsNullOrWhiteSpace(email.ConversationId)
            ? $"conversation:{email.ConversationId.Trim()}"
            : $"subject:{context.SubjectCore}";
        var dueKey = analysis.DueAt?.ToLocalTime().Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "no-due";
        var titleKey = NormalizeKey(analysis.SuggestedTitle);
        if (titleKey.Length > 80)
        {
            titleKey = titleKey[..80];
        }

        return StableHash.Create($"action|{threadKey}|{analysis.Kind}|{dueKey}|{titleKey}");
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        var withoutPrefixes = MailBodyContextBuilder.NormalizeSubject(value)
            .Replace("메일 확인:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("LLM 분석 확인 필요:", string.Empty, StringComparison.OrdinalIgnoreCase);
        var lowered = withoutPrefixes.ToLowerInvariant();
        return NonSemanticCharsRegex().Replace(lowered, string.Empty).Trim();
    }

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex NonSemanticCharsRegex();
}
