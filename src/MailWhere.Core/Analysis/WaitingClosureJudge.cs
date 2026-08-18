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
    private const string SchemaName = "mailwhere_waiting_closure";
    private static readonly JsonElement ResponseSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "shouldSuggest", "confidence", "reason" },
        properties = new Dictionary<string, object>
        {
            ["shouldSuggest"] = new { type = "boolean" },
            ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
            ["reason"] = new { type = "string", maxLength = 60 }
        }
    });

    private const string SystemPrompt = """
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
    private readonly LlmAnalysisSettings _settings;

    public LlmBackedWaitingClosureJudge(
        ILlmClient client,
        IWaitingClosureJudge? fallback = null,
        LlmAnalysisSettings? settings = null)
    {
        _client = client;
        _fallback = fallback ?? new RuleBasedWaitingClosureJudge();
        _settings = settings ?? LlmAnalysisSettings.Default;
    }

    public async Task<WaitingClosureJudgment> JudgeAsync(WaitingClosureTrigger trigger, CancellationToken cancellationToken = default)
    {
        try
        {
            var completion = await _client.CompleteJsonAsync(
                SystemPrompt,
                BuildPayload(trigger),
                cancellationToken,
                BuildRequestOptions(_settings)).ConfigureAwait(false);
            return TryParseJudgment(completion.Content, trigger, WaitingClosureDecisionSource.Llm, out var judgment)
                ? judgment
                : await _fallback.JudgeAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return await _fallback.JudgeAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static LlmRequestOptions BuildRequestOptions(LlmAnalysisSettings settings) => new(
        MaxOutputTokens: settings.MaxOutputTokens <= 0 ? 512 : Math.Clamp(settings.MaxOutputTokens, 512, 8192),
        JsonSchemaName: SchemaName,
        JsonSchema: ResponseSchema,
        StructuredOutputMode: settings.StructuredOutputMode,
        ThinkingControlMode: settings.ThinkingControlMode,
        Temperature: settings.Temperature);

    internal static string BuildProbePayload() => BuildPayload(ProbeTrigger);

    internal static bool AcceptsProbeResponse(string raw) =>
        !ContainsThinkingLeakage(raw)
        && TryParseJudgment(raw, ProbeTrigger, WaitingClosureDecisionSource.Llm, out var judgment)
        && judgment.ShouldSuggest;

    private static string BuildPayload(WaitingClosureTrigger trigger) => JsonSerializer.Serialize(new
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

    private static bool TryParseJudgment(
        string raw,
        WaitingClosureTrigger trigger,
        WaitingClosureDecisionSource source,
        out WaitingClosureJudgment judgment)
    {
        judgment = WaitingClosureJudgment.Reject(trigger.Reason, source);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("shouldSuggest", out _)
                || !document.RootElement.TryGetProperty("confidence", out _)
                || !document.RootElement.TryGetProperty("reason", out _))
            {
                return false;
            }

            var response = JsonSerializer.Deserialize<LlmClosureResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (response?.ShouldSuggest is not bool shouldSuggest)
            {
                return false;
            }

            var confidence = Math.Clamp(response.Confidence ?? trigger.Confidence, 0.0, 1.0);
            var reason = EvidencePolicy.Truncate(response.Reason) ?? trigger.Reason;
            judgment = shouldSuggest
                ? new WaitingClosureJudgment(true, confidence, reason, source)
                : WaitingClosureJudgment.Reject(reason, source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsThinkingLeakage(string raw) =>
        raw.Contains("<think>", StringComparison.OrdinalIgnoreCase)
        || raw.Contains("</think>", StringComparison.OrdinalIgnoreCase);

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

    private static readonly WaitingClosureTrigger ProbeTrigger = new(
        new LocalTaskItem(
            Guid.NewGuid(),
            "거래처 회신 대기",
            null,
            "mailwhere-closure-probe-task",
            null,
            0.8,
            "상대 답변을 기다리는 항목",
            null,
            LocalTaskStatus.Open,
            null,
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            Kind: FollowUpKind.WaitingForReply),
        new EmailSnapshot(
            "mailwhere-closure-probe-mail",
            new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
            "Vendor",
            "Re: 거래처 회신 대기",
            null,
            MailboxOwnerDisplayName: "영희",
            RecipientDisplayNames: new[] { "영희" },
            MailboxRecipientRole: MailboxRecipientRole.Direct),
        WaitingClosureTriggerKind.RecipientReply,
        0.8,
        "상대 회신이 감지되었습니다.");
}
