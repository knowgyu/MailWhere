using System.Text.RegularExpressions;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

public enum OwnershipDecision
{
    UnspecifiedOrMine,
    ExplicitlyOther
}

public static partial class OwnershipClassifier
{
    private static readonly string[] GenericAssigneeWords =
    [
        "팀",
        "전체",
        "담당",
        "담당자",
        "여러분",
        "team",
        "all",
        "everyone",
        "owner",
        "owners"
    ];

    public static OwnershipDecision Decide(EmailSnapshot email, MailBodyContext context)
    {
        if (string.IsNullOrWhiteSpace(email.MailboxOwnerDisplayName)
            || string.IsNullOrWhiteSpace(context.CurrentMessage))
        {
            return OwnershipDecision.UnspecifiedOrMine;
        }

        var assignees = ExtractExplicitAssignees(context.CurrentMessage);
        if (assignees.Count == 0)
        {
            return OwnershipDecision.UnspecifiedOrMine;
        }

        return assignees.Any(assignee => LooksLikeMailboxOwner(assignee, email.MailboxOwnerDisplayName))
            ? OwnershipDecision.UnspecifiedOrMine
            : OwnershipDecision.ExplicitlyOther;
    }

    public static IReadOnlyList<string> ExtractExplicitAssignees(string currentMessage)
    {
        var names = new List<string>();
        foreach (Match match in KoreanTitleAssigneeRegex().Matches(currentMessage))
        {
            AddIfSpecific(names, match.Groups["name"].Value);
        }

        foreach (Match match in KoreanPostpositionAssigneeRegex().Matches(currentMessage))
        {
            AddIfSpecific(names, match.Groups["name"].Value);
        }

        foreach (Match match in EnglishAssigneeRegex().Matches(currentMessage))
        {
            AddIfSpecific(names, match.Groups["name"].Value);
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool LooksLikeMailboxOwner(string assignee, string? mailboxOwnerDisplayName)
    {
        if (string.IsNullOrWhiteSpace(assignee) || string.IsNullOrWhiteSpace(mailboxOwnerDisplayName))
        {
            return false;
        }

        var normalizedAssignee = NormalizeName(assignee);
        var normalizedOwner = NormalizeName(mailboxOwnerDisplayName);
        if (normalizedAssignee.Length < 2 || normalizedOwner.Length < 2)
        {
            return false;
        }

        if (normalizedOwner.Contains(normalizedAssignee, StringComparison.OrdinalIgnoreCase)
            || normalizedAssignee.Contains(normalizedOwner, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return OwnerTokens(mailboxOwnerDisplayName)
            .Any(token => token.Length >= 2
                          && (normalizedAssignee.Contains(token, StringComparison.OrdinalIgnoreCase)
                              || token.Contains(normalizedAssignee, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> OwnerTokens(string value) =>
        NameTokenRegex().Matches(value)
            .Select(match => NormalizeName(match.Value))
            .Where(token => token.Length >= 2);

    private static void AddIfSpecific(List<string> names, string name)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length < 2 || GenericAssigneeWords.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        names.Add(name.Trim());
    }

    private static string NormalizeName(string value)
    {
        var withoutEmail = EmailAddressRegex().Replace(value, string.Empty);
        var withoutTitles = TitleRegex().Replace(withoutEmail, string.Empty);
        return NonNameRegex().Replace(withoutTitles, string.Empty).Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"(?<name>[가-힣A-Za-z][가-힣A-Za-z\s]{1,24}?)(?:님|프로|책임|선임|수석|매니저|담당|PM|PL)\s*(?:께서|께|에게|한테|이|가)?\s*.{0,12}?(?:검토|확인|회신|대응|작성|공유|전달|제출|처리|챙겨|부탁|review|reply|respond|send|submit|update|handle|check)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KoreanTitleAssigneeRegex();

    [GeneratedRegex(@"(?<name>[가-힣A-Za-z][가-힣A-Za-z\s]{1,24}?)(?:께|에게|한테)\s*.{0,12}?(?:검토|확인|회신|대응|작성|공유|전달|제출|처리|챙겨|부탁|review|reply|respond|send|submit|update|handle|check)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KoreanPostpositionAssigneeRegex();

    [GeneratedRegex(@"(?<name>[A-Z][A-Za-z]+(?:\s+[A-Z][A-Za-z]+)?)\s*,?\s+(?:please|could you|can you|review|reply|respond|send|submit|update|handle|check)", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishAssigneeRegex();

    [GeneratedRegex(@"<[^>]+>|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"(님|프로|책임|선임|수석|매니저|담당|PM|PL)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"[^가-힣A-Za-z]")]
    private static partial Regex NonNameRegex();

    [GeneratedRegex(@"[가-힣]{2,4}|[A-Za-z]{2,}")]
    private static partial Regex NameTokenRegex();
}
