using System.Text.RegularExpressions;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

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
        "(참고|공지|뉴스레터|시스템 알림|자동 발송|newsletter|no action|FYI|for your information|advertisement|unsubscribe|광고|구독)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AutomatedSender = new(
        "(no-?reply|noreply|do-?not-?reply|notification|newsletter|mailer-daemon)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AcknowledgementOnly = new(
        @"^\s*(?:(?:네|넵|예|확인했습니다|확인하였습니다|감사합니다|고맙습니다|ok|okay|thanks|thank you)[\s.!。~]*)+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        var context = MailBodyContextBuilder.Build(email);
        if (OwnershipClassifier.Decide(email, context) == OwnershipDecision.ExplicitlyOther)
        {
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.None,
                AnalysisDisposition.Ignore,
                0.9,
                string.Empty,
                "현재 작성부에서 다른 사람에게 명시적으로 배정된 요청으로 판단했습니다.",
                EvidencePolicy.Truncate(context.CurrentMessage),
                null,
                "내 업무로 분류하지 않음"));
        }

        var text = string.IsNullOrWhiteSpace(context.BodyForAnalysis)
            ? context.SubjectCore
            : context.BodyForAnalysis;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(FollowUpAnalysis.Ignore("No readable mail content."));
        }

        if (context.QuotedHistoryTrimmed && AcknowledgementOnly.IsMatch(text))
        {
            return Task.FromResult(FollowUpAnalysis.Ignore("최신 답장이 단순 확인/감사 표현이고, 오래된 인용 요청은 무시했습니다."));
        }

        var contextText = string.IsNullOrWhiteSpace(context.SubjectCore)
            ? text
            : $"{context.SubjectCore}\n{text}";
        var action = ActionKeyword.Match(text);
        var due = DueKeyword.Match(contextText);
        var meeting = MeetingKeyword.Match(contextText);
        var ignore = IgnoreKeyword.Match(contextText);
        var automatedSender = AutomatedSender.Match(email.SenderDisplay);
        var dueAt = SimpleDueDateParser.TryParse(contextText, email.ReceivedAt);

        if ((ignore.Success || automatedSender.Success) && !action.Success && !due.Success)
        {
            return Task.FromResult(new FollowUpAnalysis(
                FollowUpKind.None,
                AnalysisDisposition.Ignore,
                0.15,
                string.Empty,
                "Looks informational and has no clear action keyword.",
                EvidencePolicy.Truncate(ignore.Success ? ignore.Value : automatedSender.Value),
                null));
        }

        if (meeting.Success && (due.Success || action.Success))
        {
            var hasExplicitAction = action.Success;
            var hasExplicitDue = due.Success;
            return Task.FromResult(ApplyContextPolicy(new FollowUpAnalysis(
                FollowUpKind.Meeting,
                hasExplicitDue && hasExplicitAction ? AnalysisDisposition.AutoCreateTask : AnalysisDisposition.Review,
                hasExplicitDue && hasExplicitAction ? 0.82 : 0.62,
                BuildTitle(context.SubjectCore, meeting.Value),
                "회의/일정성 표현과 후속 조치 신호가 감지되었습니다.",
                EvidenceAround(contextText, meeting.Index),
                dueAt), context));
        }

        if (action.Success && due.Success)
        {
            return Task.FromResult(ApplyContextPolicy(new FollowUpAnalysis(
                FollowUpKind.Deadline,
                AnalysisDisposition.AutoCreateTask,
                0.90,
                BuildTitle(context.SubjectCore, action.Value),
                "명확한 요청 표현과 마감 신호가 함께 감지되었습니다.",
                EvidenceAround(text, action.Index),
                dueAt), context));
        }

        if (action.Success)
        {
            return Task.FromResult(ApplyContextPolicy(new FollowUpAnalysis(
                LooksLikeReply(action.Value) ? FollowUpKind.ReplyRequired : FollowUpKind.ActionRequested,
                AnalysisDisposition.Review,
                0.65,
                BuildTitle(context.SubjectCore, action.Value),
                "Action keyword가 감지되었지만 강한 마감 신호는 없습니다.",
                EvidenceAround(text, action.Index),
                dueAt), context));
        }

        if (due.Success)
        {
            return Task.FromResult(ApplyContextPolicy(new FollowUpAnalysis(
                FollowUpKind.ReviewNeeded,
                AnalysisDisposition.Review,
                0.45,
                BuildTitle(context.SubjectCore, due.Value),
                "마감성 표현이 있으나 명확한 요청 표현은 약합니다.",
                EvidenceAround(contextText, due.Index),
                dueAt), context));
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

    private static FollowUpAnalysis ApplyContextPolicy(FollowUpAnalysis analysis, MailBodyContext context)
    {
        if (context.Kind != MailContextKind.Forward || analysis.Disposition != AnalysisDisposition.AutoCreateTask)
        {
            return analysis;
        }

        return analysis with
        {
            Disposition = AnalysisDisposition.Review,
            Confidence = Math.Min(analysis.Confidence, 0.7),
            Reason = EvidencePolicy.Truncate($"{analysis.Reason} 단, 현재 작성부의 명시 요청이 약한 전달 메일이라 자동 등록 대신 검토로 둡니다.")
                ?? analysis.Reason
        };
    }
}
