using System.Text.RegularExpressions;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

public enum MailContextKind
{
    Simple,
    Reply,
    Forward,
    ForwardedDelegation
}

public sealed record MailBodyContext(
    string SubjectCore,
    string CurrentMessage,
    string? ForwardedContext,
    string? QuotedHistory,
    string BodyForAnalysis,
    bool CurrentSenderDelegatesForwardedContext,
    bool QuotedHistoryTrimmed,
    MailContextKind Kind);

public static partial class MailBodyContextBuilder
{
    private const int MaxCurrentChars = 4000;
    private const int MaxContextChars = 2500;

    public static MailBodyContext Build(EmailSnapshot email)
    {
        var subjectCore = NormalizeSubject(email.Subject);
        var body = NormalizeNewlines(email.Body ?? string.Empty).Trim();
        var subjectLooksForwarded = ForwardPrefixRegex().IsMatch(email.Subject);
        var subjectLooksReply = ReplyPrefixRegex().IsMatch(email.Subject);
        var boundary = FindFirstBoundary(body);

        var current = body;
        string? historical = null;
        var boundaryIsForward = subjectLooksForwarded;
        if (boundary >= 0)
        {
            current = body[..boundary].Trim();
            historical = body[boundary..].Trim();
            boundaryIsForward = subjectLooksForwarded || ForwardBoundaryRegex().IsMatch(historical);
        }

        current = TrimForPrompt(current, MaxCurrentChars);
        historical = TrimNullable(historical, MaxContextChars);

        var delegatesForward = !string.IsNullOrWhiteSpace(current) && DelegatesForwardedContextRegex().IsMatch(current);
        var forwardedContext = boundaryIsForward ? historical : null;
        var quotedHistory = boundaryIsForward ? null : historical;
        var includeForwardedContext = !string.IsNullOrWhiteSpace(forwardedContext)
                                      && (delegatesForward || subjectLooksForwarded || string.IsNullOrWhiteSpace(current));
        var bodyForAnalysis = BuildAnalysisBody(current, includeForwardedContext ? forwardedContext : null);
        var kind = includeForwardedContext && delegatesForward
            ? MailContextKind.ForwardedDelegation
            : includeForwardedContext
                ? MailContextKind.Forward
                : subjectLooksReply || !string.IsNullOrWhiteSpace(quotedHistory)
                    ? MailContextKind.Reply
                    : MailContextKind.Simple;

        return new MailBodyContext(
            subjectCore,
            current,
            includeForwardedContext ? forwardedContext : null,
            quotedHistory,
            bodyForAnalysis,
            delegatesForward,
            !string.IsNullOrWhiteSpace(quotedHistory),
            kind);
    }

    public static string NormalizeSubject(string subject)
    {
        var normalized = subject.Trim();
        while (true)
        {
            var next = SubjectPrefixRegex().Replace(normalized, string.Empty, 1).Trim();
            if (string.Equals(next, normalized, StringComparison.Ordinal))
            {
                return CollapseWhitespace(next);
            }

            normalized = next;
        }
    }

    private static string BuildAnalysisBody(string current, string? forwardedContext)
    {
        if (string.IsNullOrWhiteSpace(forwardedContext))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return $"[전달된 메일 맥락]\n{forwardedContext}";
        }

        return $"{current}\n\n[전달된 메일 맥락 — 현재 발신자가 아래 내용을 확인/대응하라고 요청한 경우에만 action 근거로 사용]\n{forwardedContext}";
    }

    private static int FindFirstBoundary(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return -1;
        }

        var match = BoundaryRegex().Match(body);
        return match.Success ? match.Index : -1;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string? TrimNullable(string? text, int maxChars) =>
        string.IsNullOrWhiteSpace(text) ? null : TrimForPrompt(text.Trim(), maxChars);

    private static string TrimForPrompt(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"^\s*((re|fw|fwd|답장|전달)\s*[:：]\s*)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubjectPrefixRegex();

    [GeneratedRegex(@"^\s*(fw|fwd|전달)\s*[:：]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForwardPrefixRegex();

    [GeneratedRegex(@"^\s*(re|답장)\s*[:：]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReplyPrefixRegex();

    [GeneratedRegex(@"(?im)^\s*(-{2,}\s*)?(original message|forwarded message|원본\s*메시지|전달된\s*메시지|보낸\s*사람\s*:|from\s*:|sent\s*:|보낸\s*날짜\s*:|제목\s*:|subject\s*:).*$")]
    private static partial Regex BoundaryRegex();

    [GeneratedRegex(@"(?im)(forwarded message|전달된\s*메시지|^\s*(fw|fwd|전달)\s*:)")]
    private static partial Regex ForwardBoundaryRegex();

    [GeneratedRegex(@"(아래|밑에|전달|포워드|forward|below|첨부|내용|건).{0,40}(확인|검토|회신|대응|처리|챙겨|부탁|review|reply|respond|handle|check|confirm)|(확인|검토|회신|대응|처리|챙겨|부탁|review|reply|respond|handle|check|confirm).{0,40}(아래|전달|포워드|forward|below|내용|건)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelegatesForwardedContextRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
