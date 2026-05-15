using MailWhere.Core.Domain;

namespace MailWhere.Core.Localization;

public static class KoreanLabels
{
    public static string Kind(FollowUpKind kind) => kind switch
    {
        FollowUpKind.None => "일반",
        FollowUpKind.ReplyRequired => "답장 필요",
        FollowUpKind.ActionRequested => "할 일",
        FollowUpKind.Deadline => "마감 있음",
        FollowUpKind.PromisedByMe => "내 약속",
        FollowUpKind.WaitingForReply => "회신 대기",
        FollowUpKind.ReviewNeeded => "검토 필요",
        FollowUpKind.Meeting => "회의",
        FollowUpKind.CalendarEvent => "일정",
        _ => "기타"
    };

    public static string Disposition(AnalysisDisposition disposition) => disposition switch
    {
        AnalysisDisposition.Ignore => "무시",
        AnalysisDisposition.Review => "검토 후보",
        AnalysisDisposition.AutoCreateTask => "자동 등록",
        _ => "알 수 없음"
    };
}
