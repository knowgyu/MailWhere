using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OutlookAiSecretary.Core.Domain;
using OutlookAiSecretary.Core.LLM;

namespace OutlookAiSecretary.Core.Analysis;

public sealed class LlmBackedFollowUpAnalyzer : IFollowUpAnalyzer
{
    private const int MaxPromptBodyChars = 6000;
    private readonly ILlmClient _llmClient;
    private readonly IFollowUpAnalyzer _fallback;

    public LlmBackedFollowUpAnalyzer(ILlmClient llmClient, IFollowUpAnalyzer? fallback = null)
    {
        _llmClient = llmClient;
        _fallback = fallback ?? new RuleBasedFollowUpAnalyzer();
    }

    public async Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        var fallback = await _fallback.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false);
        try
        {
            var raw = await _llmClient.CompleteJsonAsync(SystemPrompt, BuildPayload(email), cancellationToken).ConfigureAwait(false);
            return TryParse(raw, email, fallback, out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string BuildPayload(EmailSnapshot email)
    {
        var body = email.Body ?? string.Empty;
        if (body.Length > MaxPromptBodyChars)
        {
            body = body[..MaxPromptBodyChars] + "…";
        }

        return JsonSerializer.Serialize(
            new
            {
                language = "ko-KR",
                now = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                receivedAt = email.ReceivedAt.ToString("O", CultureInfo.InvariantCulture),
                senderDisplay = email.SenderDisplay,
                subject = email.Subject,
                body
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static bool TryParse(string raw, EmailSnapshot email, FollowUpAnalysis fallback, out FollowUpAnalysis parsed)
    {
        parsed = fallback;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var response = JsonSerializer.Deserialize<LlmFollowUpResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            });

            if (response is null)
            {
                return false;
            }

            var confidence = Math.Clamp(response.Confidence ?? fallback.Confidence, 0, 1);
            var disposition = response.Disposition ?? fallback.Disposition;
            var kind = response.Kind ?? fallback.Kind;
            var dueAt = TryParseDate(response.DueAt) ?? fallback.DueAt;
            var title = EvidencePolicy.Truncate(response.SuggestedTitle) ?? fallback.SuggestedTitle;
            if (string.IsNullOrWhiteSpace(title) && disposition != AnalysisDisposition.Ignore)
            {
                title = EvidencePolicy.Truncate($"메일 확인: {email.Subject}") ?? "메일 확인";
            }

            parsed = new FollowUpAnalysis(
                kind,
                disposition,
                confidence,
                title,
                EvidencePolicy.Truncate(response.Reason) ?? fallback.Reason,
                EvidencePolicy.Truncate(response.EvidenceSnippet) ?? fallback.EvidenceSnippet,
                dueAt,
                EvidencePolicy.Truncate(response.Summary) ?? fallback.Summary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DateTimeOffset? TryParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;

    private const string SystemPrompt = """
        당신은 한국어 업무 메일에서 사용자가 해야 할 action item, 회의/일정성 항목, 답장 필요 여부, 마감일을 추출하는 로컬 비서입니다.
        Outlook 메일을 수정하지 않습니다. 첨부파일을 열지 않습니다. 근거는 짧게만 반환합니다.
        반드시 JSON object 하나만 반환하세요.
        스키마:
        {
          "kind": "none|replyRequired|actionRequested|deadline|waitingForReply|reviewNeeded|meeting|calendarEvent",
          "disposition": "ignore|review|autoCreateTask",
          "confidence": 0.0,
          "suggestedTitle": "한국어 한 줄 제목",
          "reason": "짧은 판단 이유",
          "evidenceSnippet": "메일에서 필요한 짧은 근거",
          "dueAt": "ISO-8601 또는 null",
          "summary": "요약 한 줄"
        }
        확실하지 않으면 disposition은 review로 두세요. 뉴스레터/공지/FYI는 ignore로 두세요.
        """;

    private sealed record LlmFollowUpResponse(
        FollowUpKind? Kind,
        AnalysisDisposition? Disposition,
        double? Confidence,
        string? SuggestedTitle,
        string? Reason,
        string? EvidenceSnippet,
        string? DueAt,
        string? Summary);
}
