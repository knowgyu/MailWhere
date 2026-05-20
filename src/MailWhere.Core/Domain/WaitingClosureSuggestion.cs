namespace MailWhere.Core.Domain;

public enum WaitingClosureTriggerKind
{
    RecipientReply,
    UserAcknowledgement
}

public enum WaitingClosureDecisionSource
{
    Rule,
    Llm
}

public enum WaitingClosureResolution
{
    Archived,
    Kept
}

public sealed record WaitingClosureSuggestion(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    string TriggerSourceHash,
    WaitingClosureTriggerKind TriggerKind,
    WaitingClosureDecisionSource DecisionSource,
    double Confidence,
    string Reason,
    DateTimeOffset TriggeredAt,
    DateTimeOffset CreatedAt)
{
    public string ActionText => TriggerKind == WaitingClosureTriggerKind.UserAcknowledgement
        ? "확인 답장을 보낸 것으로 보입니다. 보관할까요?"
        : "회신이 감지되었습니다. 보관할까요?";
}
