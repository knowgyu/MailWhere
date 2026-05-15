using MailWhere.Core.Domain;

namespace MailWhere.Core.Analysis;

public static class RecipientTriagePolicy
{
    public static FollowUpAnalysis Apply(EmailSnapshot email, FollowUpAnalysis analysis)
    {
        if (analysis.Disposition == AnalysisDisposition.Ignore || analysis.Kind == FollowUpKind.None)
        {
            return analysis;
        }

        if (IsSchedule(analysis.Kind))
        {
            return analysis with
            {
                Disposition = AnalysisDisposition.AutoCreateTask,
                Reason = EvidencePolicy.Truncate(analysis.Reason) ?? "일정성 메일입니다."
            };
        }

        if (email.MailboxRecipientRole == MailboxRecipientRole.Cc)
        {
            return FollowUpAnalysis.Ignore("참조 수신 메일의 비일정성 요청은 업무보드에 올리지 않습니다.");
        }

        if (email.MailboxRecipientRole is MailboxRecipientRole.Bcc or MailboxRecipientRole.Other
            && IsActionable(analysis.Kind))
        {
            return analysis with
            {
                Disposition = AnalysisDisposition.Review,
                Confidence = Math.Min(analysis.Confidence, 0.55),
                Reason = EvidencePolicy.Truncate(analysis.Reason) ?? "수신/참조 여부를 확정하지 못해 자동 등록하지 않았습니다."
            };
        }

        if (email.MailboxRecipientRole == MailboxRecipientRole.Direct
            && analysis.Disposition == AnalysisDisposition.Review
            && IsActionable(analysis.Kind))
        {
            return analysis with
            {
                Disposition = AnalysisDisposition.AutoCreateTask,
                Confidence = Math.Max(analysis.Confidence, 0.62),
                Reason = EvidencePolicy.Truncate(analysis.Reason) ?? "수신 메일의 후속 조치입니다."
            };
        }

        return analysis;
    }

    private static bool IsSchedule(FollowUpKind kind) =>
        kind is FollowUpKind.Meeting or FollowUpKind.CalendarEvent;

    private static bool IsActionable(FollowUpKind kind) =>
        kind is FollowUpKind.ReplyRequired
            or FollowUpKind.ActionRequested
            or FollowUpKind.Deadline
            or FollowUpKind.WaitingForReply;
}
