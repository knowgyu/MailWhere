using System.Globalization;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailWhere.Core.Domain;
using MailWhere.Core.LLM;

namespace MailWhere.Core.Analysis;

public sealed class LlmBackedFollowUpAnalyzer : IFollowUpAnalyzer, IAnalysisTelemetrySource
{
    private const int MaxPromptBodyChars = 6000;
    private readonly ILlmClient _llmClient;
    private readonly IFollowUpAnalyzer _fallback;
    private readonly LlmFallbackPolicy _fallbackPolicy;
    private readonly object _telemetryLock = new();
    private int _llmAttemptCount;
    private int _llmSuccessCount;
    private int _llmFallbackCount;
    private int _llmFailureCount;
    private TimeSpan _totalLlmDuration = TimeSpan.Zero;
    private string? _lastFailureCode;

    public LlmBackedFollowUpAnalyzer(
        ILlmClient llmClient,
        IFollowUpAnalyzer? fallback = null,
        LlmFallbackPolicy fallbackPolicy = LlmFallbackPolicy.LlmThenRules)
    {
        _llmClient = llmClient;
        _fallback = fallback ?? new RuleBasedFollowUpAnalyzer();
        _fallbackPolicy = fallbackPolicy;
    }

    public async Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _llmClient.CompleteJsonAsync(SystemPrompt, BuildPayload(email), cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (TryParse(raw, email, out var parsed))
            {
                RecordSuccess(stopwatch.Elapsed);
                return parsed;
            }

            RecordFailure("invalid-json", stopwatch.Elapsed);
            return await HandleLlmFailureAsync(email, "invalid-json", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            var failureCode = SanitizeFailureCode(ex);
            RecordFailure(failureCode, stopwatch.Elapsed);
            return await HandleLlmFailureAsync(email, failureCode, cancellationToken).ConfigureAwait(false);
        }
    }

    public AnalysisTelemetry GetTelemetrySnapshot()
    {
        lock (_telemetryLock)
        {
            return new AnalysisTelemetry(
                _llmAttemptCount,
                _llmSuccessCount,
                _llmFallbackCount,
                _llmFailureCount,
                _totalLlmDuration,
                _lastFailureCode);
        }
    }

    private static string BuildPayload(EmailSnapshot email)
    {
        var context = MailBodyContextBuilder.Build(email);
        var body = context.BodyForAnalysis;
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
                subjectCore = context.SubjectCore,
                mailboxOwnerDisplayName = email.MailboxOwnerDisplayName,
                recipientDisplayNames = email.RecipientDisplayNames ?? Array.Empty<string>(),
                conversationIdPresent = !string.IsNullOrWhiteSpace(email.ConversationId),
                contextKind = context.Kind.ToString(),
                currentSenderDelegatesForwardedContext = context.CurrentSenderDelegatesForwardedContext,
                quotedHistoryTrimmed = context.QuotedHistoryTrimmed,
                currentMessage = context.CurrentMessage,
                forwardedContext = context.ForwardedContext,
                quotedHistory = context.QuotedHistory,
                bodyForAnalysis = body
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private async Task<FollowUpAnalysis> HandleLlmFailureAsync(EmailSnapshot email, string failureCode, CancellationToken cancellationToken)
    {
        if (_fallbackPolicy == LlmFallbackPolicy.LlmThenRules)
        {
            var fallback = await _fallback.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false);
            RecordFallback();
            return fallback;
        }

        return BuildFailureReview(email, failureCode);
    }

    private static FollowUpAnalysis BuildFailureReview(EmailSnapshot email, string failureCode)
    {
        var title = EvidencePolicy.Truncate($"LLM 분석 확인 필요: {email.Subject}") ?? "LLM 분석 확인 필요";
        return new FollowUpAnalysis(
            FollowUpKind.ReviewNeeded,
            AnalysisDisposition.Review,
            0.2,
            title,
            $"LLM 분석 실패({failureCode})로 자동 등록하지 않았습니다.",
            null,
            null,
            "LLM endpoint 상태를 확인한 뒤 다시 스캔하세요.");
    }

    private static bool TryParse(string raw, EmailSnapshot email, out FollowUpAnalysis parsed)
    {
        parsed = BuildFailureReview(email, "empty-response");
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

            var confidence = Math.Clamp(response.Confidence ?? 0.5, 0, 1);
            var disposition = response.Disposition ?? AnalysisDisposition.Review;
            var kind = response.Kind ?? FollowUpKind.ReviewNeeded;
            var context = MailBodyContextBuilder.Build(email);
            if (OwnershipClassifier.Decide(email, context) == OwnershipDecision.ExplicitlyOther)
            {
                parsed = new FollowUpAnalysis(
                    FollowUpKind.None,
                    AnalysisDisposition.Ignore,
                    Math.Max(confidence, 0.75),
                    string.Empty,
                    "현재 작성부에서 다른 사람에게 명시적으로 배정된 요청으로 판단했습니다.",
                    EvidencePolicy.Truncate(response.EvidenceSnippet ?? context.CurrentMessage),
                    null,
                    "내 업무로 분류하지 않음");
                return true;
            }

            if (response.AssignedToMailboxUser == false && !string.IsNullOrWhiteSpace(response.ExplicitAssignee))
            {
                disposition = AnalysisDisposition.Review;
                confidence = Math.Min(confidence, 0.6);
            }

            disposition = ApplyOriginPolicy(disposition, response, context);
            var dueAt = TryParseDate(response.DueAt);
            var title = EvidencePolicy.Truncate(response.SuggestedTitle);
            if (string.IsNullOrWhiteSpace(title) && disposition != AnalysisDisposition.Ignore)
            {
                title = EvidencePolicy.Truncate($"메일 확인: {context.SubjectCore}") ?? "메일 확인";
            }
            else if (string.IsNullOrWhiteSpace(title))
            {
                title = "후속 조치 없음";
            }

            parsed = new FollowUpAnalysis(
                kind,
                disposition,
                confidence,
                title,
                EvidencePolicy.Truncate(response.Reason) ?? "LLM 분석 결과",
                EvidencePolicy.Truncate(response.EvidenceSnippet),
                dueAt,
                EvidencePolicy.Truncate(response.Summary));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DateTimeOffset? TryParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;

    private void RecordSuccess(TimeSpan elapsed)
    {
        lock (_telemetryLock)
        {
            _llmAttemptCount++;
            _llmSuccessCount++;
            _totalLlmDuration += elapsed;
        }
    }

    private void RecordFailure(string failureCode, TimeSpan elapsed)
    {
        lock (_telemetryLock)
        {
            _llmAttemptCount++;
            _llmFailureCount++;
            _totalLlmDuration += elapsed;
            _lastFailureCode = failureCode;
        }
    }

    private void RecordFallback()
    {
        lock (_telemetryLock)
        {
            _llmFallbackCount++;
        }
    }

    private static string SanitizeFailureCode(Exception ex) =>
        ex switch
        {
            TaskCanceledException => "timeout",
            HttpRequestException => "http-error",
            JsonException => "invalid-json",
            InvalidOperationException => "invalid-settings",
            _ => ex.GetType().Name
        };

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
          "summary": "요약 한 줄",
          "actionOrigin": "currentMessage|forwardedContext|quotedHistory|none",
          "currentSenderRequested": true,
          "explicitAssignee": "명시 대상자 또는 null",
          "assignedToMailboxUser": true
        }
        `currentMessage`는 이번 메일 작성자가 새로 쓴 부분입니다.
        `forwardedContext`는 현재 작성자가 아래/전달 내용을 확인·대응하라고 한 경우에만 action 근거로 사용하세요.
        `quotedHistory`에만 있는 오래된 요청은 자동 등록하지 말고 currentSenderRequested=false로 두세요.
        mailboxOwnerDisplayName이 있고 현재 작성부가 명시적으로 다른 사람에게 일을 배정하면 ignore로 두세요.
        명시 대상자가 없거나 mailboxOwnerDisplayName을 가리키면 사용자의 업무로 간주하세요.
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
        string? Summary,
        string? ActionOrigin,
        bool? CurrentSenderRequested,
        string? ExplicitAssignee,
        bool? AssignedToMailboxUser);

    private static AnalysisDisposition ApplyOriginPolicy(
        AnalysisDisposition disposition,
        LlmFollowUpResponse response,
        MailBodyContext context)
    {
        if (disposition == AnalysisDisposition.Ignore)
        {
            return disposition;
        }

        var origin = response.ActionOrigin ?? string.Empty;
        var currentRequested = response.CurrentSenderRequested == true || context.CurrentSenderDelegatesForwardedContext;
        if (origin.Equals("quotedHistory", StringComparison.OrdinalIgnoreCase) && !currentRequested)
        {
            return AnalysisDisposition.Review;
        }

        if (origin.Equals("forwardedContext", StringComparison.OrdinalIgnoreCase)
            && !currentRequested
            && disposition == AnalysisDisposition.AutoCreateTask)
        {
            return AnalysisDisposition.Review;
        }

        return disposition;
    }
}
