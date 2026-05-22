using System.Text.Json;
using MailWhere.Core.Domain;
using MailWhere.Core.LLM;

namespace MailWhere.Core.Analysis;

public sealed record WaitingClosureTrigger(
    LocalTaskItem Task,
    EmailSnapshot Email,
    WaitingClosureTriggerKind Kind,
    double Confidence,
    string Reason);

public sealed record WaitingClosureJudgment(
    bool ShouldSuggest,
    double Confidence,
    string Reason,
    WaitingClosureDecisionSource Source)
{
    public static WaitingClosureJudgment Rule(double confidence, string reason) => new(true, confidence, reason, WaitingClosureDecisionSource.Rule);
    public static WaitingClosureJudgment Reject(string reason, WaitingClosureDecisionSource source) => new(false, 0, reason, source);
}

public interface IWaitingClosureJudge
{
    Task<WaitingClosureJudgment> JudgeAsync(WaitingClosureTrigger trigger, CancellationToken cancellationToken = default);
}

public sealed class RuleBasedWaitingClosureJudge : IWaitingClosureJudge
{
    public Task<WaitingClosureJudgment> JudgeAsync(WaitingClosureTrigger trigger, CancellationToken cancellationToken = default) =>
        Task.FromResult(WaitingClosureJudgment.Rule(trigger.Confidence, trigger.Reason));
}

public sealed class LlmBackedWaitingClosureJudge : IWaitingClosureJudge
{
    private const string SystemPrompt = """
        /no_think
        한국어 메일 대기 업무 closure 판단 전용 비서입니다. JSON object 하나만 반환하세요.
        목표: 사용자의 WaitingForReply 항목을 보관 제안해도 되는지 보수적으로 판단합니다.
        규칙:
        - 실제 요청을 만족하는 자료/답변/완료 통지가 있으면 shouldSuggest true.
        - 사용자가 같은 thread에서 확인/수령/감사 답장을 보냈으면 shouldSuggest true.
        - "확인해보겠습니다", "진행 중", 단순 수신 확인, 일부 인원만 회신처럼 아직 기다려야 하면 false.
        - 자동 보관하지 않고 사용자에게 물어볼 제안만 만드는 상황입니다.
        - reason은 한국어 60자 이내.
        스키마: {"shouldSuggest": true|false, "confidence": 0.0, "reason": "..."}
        """;

    private readonly ILlmClient _client;
    private readonly IWaitingClosureJudge _fallback;

    public LlmBackedWaitingClosureJudge(ILlmClient client, IWaitingClosureJudge? fallback = null)
    {
        _client = client;
        _fallback = fallback ?? new RuleBasedWaitingClosureJudge();
    }

    public async Task<WaitingClosureJudgment> JudgeAsync(WaitingClosureTrigger trigger, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                task = new
                {
                    title = FollowUpPresentation.ActionTitle(trigger.Task.Title),
                    reason = trigger.Task.Reason,
                    dueAt = trigger.Task.DueAt,
                    expectedRecipients = trigger.Task.SourceRecipientDisplayNames
                },
                trigger = new
                {
                    kind = trigger.Kind.ToString(),
                    sender = trigger.Email.SenderDisplay,
                    subject = trigger.Email.Subject,
                    bodySnippet = TruncateForPrompt(trigger.Email.Body, 1200),
                    sentAt = trigger.Email.ReceivedAt,
                    ruleReason = trigger.Reason
                }
            });
            var completion = await _client.CompleteJsonAsync(SystemPrompt, payload, cancellationToken, new LlmRequestOptions(MaxOutputTokens: 256)).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<LlmClosureResponse>(completion.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (response?.ShouldSuggest is not bool shouldSuggest)
            {
                return await _fallback.JudgeAsync(trigger, cancellationToken).ConfigureAwait(false);
            }

            var confidence = Math.Clamp(response.Confidence ?? trigger.Confidence, 0.0, 1.0);
            var reason = EvidencePolicy.Truncate(response.Reason) ?? trigger.Reason;
            return shouldSuggest
                ? new WaitingClosureJudgment(true, confidence, reason, WaitingClosureDecisionSource.Llm)
                : WaitingClosureJudgment.Reject(reason, WaitingClosureDecisionSource.Llm);
        }
        catch
        {
            return await _fallback.JudgeAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record LlmClosureResponse(bool? ShouldSuggest, double? Confidence, string? Reason);

    private static string? TruncateForPrompt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxChars
            ? normalized
            : normalized[..maxChars].TrimEnd() + "…";
    }
}
