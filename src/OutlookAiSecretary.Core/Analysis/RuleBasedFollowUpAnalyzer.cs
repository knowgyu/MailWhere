using System.Text.RegularExpressions;
using OutlookAiSecretary.Core.Domain;

namespace OutlookAiSecretary.Core.Analysis;

public sealed class RuleBasedFollowUpAnalyzer : IFollowUpAnalyzer
{
    private static readonly Regex ActionKeyword = new(
        "(부탁|요청|확인|검토|회신|공유|전달|작성|수정|제출|챙겨|대응|please|could you|can you|review|reply|respond|send|submit|update|action required)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DueKeyword = new(
        "(오늘|내일|금일|익일|이번 주|금요일|월요일|화요일|수요일|목요일|토요일|일요일|까지|마감|due|by tomorrow|by today|by friday|deadline|EOD)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MeetingKeyword = new(
        "(회의|미팅|콜|화상|일정|참석|초대|meeting|sync|call|calendar|invite)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IgnoreKeyword = new(
        "(참고|공지|newsletter|no action|FYI|for your information|광고|구독)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        var text = $"{email.Subject}\n{email.Body ?? string.Empty}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(FollowUpAnalysis.Ignore("No readable mail content."));
        }

        var action = ActionKeyword.Match(text);
        var due = DueKeyword.Match(text);
        var meeting = MeetingKeyword.Match(text);
        var ignore = IgnoreKeyword.Match(text);
        var dueAt = SimpleDueDateParser.TryParse(text, email.ReceivedAt);

        if (ignore.Success && !action.Success)
        {
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.None,
                AnalysisDisposition.Ignore,
                0.15,
                string.Empty,
                "Looks informational and has no clear action keyword.",
                EvidencePolicy.Truncate(ignore.Value),
                null));
        }

        if (meeting.Success && (due.Success || action.Success))
        {
            var hasExplicitAction = action.Success;
            var hasExplicitDue = due.Success;
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.Meeting,
                hasExplicitDue && hasExplicitAction ? AnalysisDisposition.AutoCreateTask : AnalysisDisposition.Review,
                hasExplicitDue && hasExplicitAction ? 0.82 : 0.62,
                BuildTitle(email.Subject, meeting.Value),
                "회의/일정성 표현과 후속 조치 신호가 감지되었습니다.",
                EvidenceAround(text, meeting.Index),
                dueAt));
        }

        if (action.Success && due.Success)
        {
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.Deadline,
                AnalysisDisposition.AutoCreateTask,
                0.90,
                BuildTitle(email.Subject, action.Value),
                "명확한 요청 표현과 마감 신호가 함께 감지되었습니다.",
                EvidenceAround(text, action.Index),
                dueAt));
        }

        if (action.Success)
        {
            return Task.FromResult(new FollowUpAnalysis(
                LooksLikeReply(action.Value) ? FollowUpKind.ReplyRequired : FollowUpKind.ActionRequested,
                AnalysisDisposition.Review,
                0.65,
                BuildTitle(email.Subject, action.Value),
                "Action keyword가 감지되었지만 강한 마감 신호는 없습니다.",
                EvidenceAround(text, action.Index),
                dueAt));
        }

        if (due.Success)
        {
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.ReviewNeeded,
                AnalysisDisposition.Review,
                0.45,
                BuildTitle(email.Subject, due.Value),
                "마감성 표현이 있으나 명확한 요청 표현은 약합니다.",
                EvidenceAround(text, due.Index),
                dueAt));
        }

        return Task.FromResult(FollowUpAnalysis.Ignore("후속 조치 신호가 감지되지 않았습니다."));
    }

    private static bool LooksLikeReply(string keyword) =>
        keyword.Contains("회신", StringComparison.OrdinalIgnoreCase)
        || keyword.Contains("reply", StringComparison.OrdinalIgnoreCase)
        || keyword.Contains("respond", StringComparison.OrdinalIgnoreCase);

    private static string BuildTitle(string subject, string keyword)
    {
        var cleanSubject = string.IsNullOrWhiteSpace(subject) ? "email" : subject.Trim();
        return EvidencePolicy.Truncate($"메일 확인: {cleanSubject}") ?? $"메일 확인: {keyword}";
    }

    private static string? EvidenceAround(string text, int index)
    {
        var start = Math.Max(0, index - 80);
        var length = Math.Min(text.Length - start, 200);
        return EvidencePolicy.Truncate(text.Substring(start, length));
    }
}
