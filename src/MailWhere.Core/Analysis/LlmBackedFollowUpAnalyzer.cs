using System.Globalization;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailWhere.Core.Domain;
using MailWhere.Core.LLM;

namespace MailWhere.Core.Analysis;

public sealed class LlmBackedFollowUpAnalyzer : IFollowUpBatchAnalyzer, IAnalysisTelemetrySource
{
    private const int MaxCurrentMessageChars = 1300;
    private const int MaxForwardedContextChars = 900;
    private const int MaxQuotedPreviewChars = 240;
    private const int DefaultBatchSize = 8;
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

    public int PreferredBatchSize => DefaultBatchSize;

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
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var failureCode = SanitizeFailureCode(ex);
            RecordFailure(failureCode, stopwatch.Elapsed);
            return await HandleLlmFailureAsync(email, failureCode, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<FollowUpAnalysis>> AnalyzeBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
        {
            return Array.Empty<FollowUpAnalysis>();
        }

        if (emails.Count == 1)
        {
            return new[] { await AnalyzeAsync(emails[0], cancellationToken).ConfigureAwait(false) };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _llmClient.CompleteJsonAsync(BatchSystemPrompt, BuildBatchPayload(emails), cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (TryParseBatch(raw, emails, out var parsed))
            {
                var missingItemCount = parsed.Count(item => item.IsTransientLlmFailureReview);
                RecordBatchCompletion(stopwatch.Elapsed, parsed.Count - missingItemCount, missingItemCount, "partial-batch");
                return missingItemCount > 0
                    ? await ApplyPartialBatchFallbackAsync(emails, parsed, cancellationToken).ConfigureAwait(false)
                    : parsed;
            }

            RecordFailure("invalid-json", stopwatch.Elapsed, emails.Count);
            return await HandleBatchLlmFailureAsync(emails, "invalid-json", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var failureCode = SanitizeFailureCode(ex);
            RecordFailure(failureCode, stopwatch.Elapsed, emails.Count);
            return await HandleBatchLlmFailureAsync(emails, failureCode, cancellationToken).ConfigureAwait(false);
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
                mailboxRecipientRole = email.MailboxRecipientRole.ToString(),
                conversationIdPresent = !string.IsNullOrWhiteSpace(email.ConversationId),
                contextKind = context.Kind.ToString(),
                currentSenderDelegatesForwardedContext = context.CurrentSenderDelegatesForwardedContext,
                quotedHistoryTrimmed = context.QuotedHistoryTrimmed,
                currentMessage = TrimForPayload(context.CurrentMessage, MaxCurrentMessageChars),
                forwardedContext = TrimForPayload(context.ForwardedContext, MaxForwardedContextChars),
                quotedHistoryPresent = !string.IsNullOrWhiteSpace(context.QuotedHistory),
                quotedHistoryPreview = TrimForPayload(context.QuotedHistory, MaxQuotedPreviewChars)
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static string BuildBatchPayload(IReadOnlyList<EmailSnapshot> emails)
    {
        var items = emails.Select((email, index) =>
        {
            var context = MailBodyContextBuilder.Build(email);
            return new
            {
                id = index.ToString(CultureInfo.InvariantCulture),
                receivedAt = email.ReceivedAt.ToString("O", CultureInfo.InvariantCulture),
                senderDisplay = email.SenderDisplay,
                subject = TrimForPayload(email.Subject, 180),
                subjectCore = context.SubjectCore,
                mailboxOwnerDisplayName = email.MailboxOwnerDisplayName,
                recipientDisplayNames = email.RecipientDisplayNames ?? Array.Empty<string>(),
                mailboxRecipientRole = email.MailboxRecipientRole.ToString(),
                contextKind = context.Kind.ToString(),
                currentSenderDelegatesForwardedContext = context.CurrentSenderDelegatesForwardedContext,
                quotedHistoryTrimmed = context.QuotedHistoryTrimmed,
                currentMessage = TrimForPayload(context.CurrentMessage, MaxCurrentMessageChars),
                forwardedContext = TrimForPayload(context.ForwardedContext, MaxForwardedContextChars),
                quotedHistoryPresent = !string.IsNullOrWhiteSpace(context.QuotedHistory),
                quotedHistoryPreview = TrimForPayload(context.QuotedHistory, MaxQuotedPreviewChars)
            };
        }).ToArray();

        return JsonSerializer.Serialize(
            new
            {
                language = "ko-KR",
                now = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                instruction = "각 items[]를 독립적으로 분석하고 입력과 같은 개수, 같은 id로 짧은 JSON 결과를 반환하세요. /no_think",
                items
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static string? TrimForPayload(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars].TrimEnd() + "…";
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

    private async Task<IReadOnlyList<FollowUpAnalysis>> HandleBatchLlmFailureAsync(IReadOnlyList<EmailSnapshot> emails, string failureCode, CancellationToken cancellationToken)
    {
        if (_fallbackPolicy == LlmFallbackPolicy.LlmThenRules)
        {
            var fallbackResults = new List<FollowUpAnalysis>(emails.Count);
            foreach (var email in emails)
            {
                fallbackResults.Add(await _fallback.AnalyzeAsync(email, cancellationToken).ConfigureAwait(false));
                RecordFallback();
            }

            return fallbackResults;
        }

        return emails.Select(email => BuildFailureReview(email, failureCode)).ToArray();
    }

    private async Task<IReadOnlyList<FollowUpAnalysis>> ApplyPartialBatchFallbackAsync(
        IReadOnlyList<EmailSnapshot> emails,
        IReadOnlyList<FollowUpAnalysis> parsed,
        CancellationToken cancellationToken)
    {
        if (_fallbackPolicy != LlmFallbackPolicy.LlmThenRules)
        {
            return parsed;
        }

        var merged = new List<FollowUpAnalysis>(parsed.Count);
        for (var i = 0; i < parsed.Count; i++)
        {
            if (parsed[i].IsTransientLlmFailureReview)
            {
                merged.Add(await _fallback.AnalyzeAsync(emails[i], cancellationToken).ConfigureAwait(false));
                RecordFallback();
            }
            else
            {
                merged.Add(parsed[i]);
            }
        }

        return merged;
    }

    private static FollowUpAnalysis BuildFailureReview(EmailSnapshot email, string failureCode)
    {
        return new FollowUpAnalysis(
            FollowUpKind.ReviewNeeded,
            AnalysisDisposition.Review,
            0.2,
            "LLM 분석 재시도 필요",
            $"LLM 분석 실패({failureCode})로 자동 등록하지 않았습니다.",
            null,
            null,
            "LLM endpoint 상태를 확인한 뒤 다시 메일을 확인하세요.");
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

            var originDowngraded = false;
            var beforeOriginDisposition = disposition;
            disposition = ApplyOriginPolicy(disposition, response, context);
            if (beforeOriginDisposition == AnalysisDisposition.AutoCreateTask
                && disposition == AnalysisDisposition.Review)
            {
                originDowngraded = true;
            }
            var dueAt = TryParseDate(response.DueAt);
            var title = EvidencePolicy.Truncate(response.SuggestedTitle);
            if (string.IsNullOrWhiteSpace(title) && disposition != AnalysisDisposition.Ignore)
            {
                title = FollowUpPresentation.ActionTitle(context.SubjectCore);
            }
            else if (string.IsNullOrWhiteSpace(title))
            {
                title = "후속 조치 없음";
            }
            else
            {
                title = FollowUpPresentation.ActionTitle(title);
            }

            var analysis = new FollowUpAnalysis(
                kind,
                disposition,
                confidence,
                title,
                EvidencePolicy.Truncate(response.Reason) ?? "LLM 분석 결과",
                EvidencePolicy.Truncate(response.EvidenceSnippet),
                dueAt,
                EvidencePolicy.Truncate(response.Summary));
            parsed = originDowngraded ? analysis : RecipientTriagePolicy.Apply(email, analysis);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseBatch(string raw, IReadOnlyList<EmailSnapshot> emails, out IReadOnlyList<FollowUpAnalysis> parsed)
    {
        parsed = Array.Empty<FollowUpAnalysis>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };

            using var document = JsonDocument.Parse(raw);
            IReadOnlyList<LlmFollowUpResponse>? items = null;
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("items", out var itemsElement))
            {
                items = JsonSerializer.Deserialize<IReadOnlyList<LlmFollowUpResponse>>(itemsElement.GetRawText(), jsonOptions);
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                items = JsonSerializer.Deserialize<IReadOnlyList<LlmFollowUpResponse>>(document.RootElement.GetRawText(), jsonOptions);
            }

            if (items is null || items.Count == 0)
            {
                return false;
            }

            var results = new List<FollowUpAnalysis>(emails.Count);
            if (items.All(item => string.IsNullOrWhiteSpace(item.Id)) && items.Count == emails.Count)
            {
                for (var i = 0; i < emails.Count; i++)
                {
                    results.Add(ParseResponseItem(items[i], emails[i]));
                }
            }
            else
            {
                var byId = BuildTrustedBatchIdMap(items, emails.Count);
                for (var i = 0; i < emails.Count; i++)
                {
                    if (!byId.TryGetValue(i, out var item))
                    {
                        results.Add(BuildFailureReview(emails[i], "missing-batch-item"));
                        continue;
                    }

                    results.Add(ParseResponseItem(item, emails[i]));
                }
            }

            parsed = results;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FollowUpAnalysis ParseResponseItem(LlmFollowUpResponse response, EmailSnapshot email)
    {
        var confidence = Math.Clamp(response.Confidence ?? 0.5, 0, 1);
        var disposition = response.Disposition ?? AnalysisDisposition.Review;
        var kind = response.Kind ?? FollowUpKind.ReviewNeeded;
        var context = MailBodyContextBuilder.Build(email);
        if (OwnershipClassifier.Decide(email, context) == OwnershipDecision.ExplicitlyOther)
        {
            return new FollowUpAnalysis(
                FollowUpKind.None,
                AnalysisDisposition.Ignore,
                Math.Max(confidence, 0.75),
                string.Empty,
                "현재 작성부에서 다른 사람에게 명시적으로 배정된 요청으로 판단했습니다.",
                EvidencePolicy.Truncate(response.EvidenceSnippet ?? context.CurrentMessage),
                null,
                "내 업무로 분류하지 않음");
        }

        if (response.AssignedToMailboxUser == false && !string.IsNullOrWhiteSpace(response.ExplicitAssignee))
        {
            disposition = AnalysisDisposition.Review;
            confidence = Math.Min(confidence, 0.6);
        }

        var originDowngraded = false;
        var beforeOriginDisposition = disposition;
        disposition = ApplyOriginPolicy(disposition, response, context);
        if (beforeOriginDisposition == AnalysisDisposition.AutoCreateTask
            && disposition == AnalysisDisposition.Review)
        {
            originDowngraded = true;
        }
        var dueAt = TryParseDate(response.DueAt);
        var title = EvidencePolicy.Truncate(response.SuggestedTitle);
        if (string.IsNullOrWhiteSpace(title) && disposition != AnalysisDisposition.Ignore)
        {
            title = FollowUpPresentation.ActionTitle(context.SubjectCore);
        }
        else if (string.IsNullOrWhiteSpace(title))
        {
            title = "후속 조치 없음";
        }
        else
        {
            title = FollowUpPresentation.ActionTitle(title);
        }

        var analysis = new FollowUpAnalysis(
            kind,
            disposition,
            confidence,
            title,
            EvidencePolicy.Truncate(response.Reason) ?? "LLM 분석 결과",
            EvidencePolicy.Truncate(response.EvidenceSnippet),
            dueAt,
            EvidencePolicy.Truncate(response.Summary));
        return originDowngraded ? analysis : RecipientTriagePolicy.Apply(email, analysis);
    }

    private static IReadOnlyDictionary<int, LlmFollowUpResponse> BuildTrustedBatchIdMap(
        IReadOnlyList<LlmFollowUpResponse> orderedItems,
        int expectedCount)
    {
        var parsed = orderedItems
            .Select((item, ordinal) => new BatchIdCandidate(item, ordinal, TryParseBatchId(item.Id)))
            .ToArray();
        var valid = parsed
            .Where(candidate => candidate.ParsedId is >= 0 && candidate.ParsedId < expectedCount)
            .ToArray();
        var idsAreCompleteAndUnique = orderedItems.Count == expectedCount
            && valid.Length == expectedCount
            && valid.Select(candidate => candidate.ParsedId!.Value).Distinct().Count() == expectedCount;
        if (idsAreCompleteAndUnique)
        {
            return valid.ToDictionary(candidate => candidate.ParsedId!.Value, candidate => candidate.Item);
        }

        return valid
            .Where(candidate => candidate.ParsedId == candidate.Ordinal)
            .GroupBy(candidate => candidate.ParsedId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Item);
    }

    private static int? TryParseBatchId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = id.Trim();
        if (normalized.StartsWith('m'))
        {
            normalized = normalized[1..];
        }

        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private sealed record BatchIdCandidate(LlmFollowUpResponse Item, int Ordinal, int? ParsedId);

    private static DateTimeOffset? TryParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;

    private void RecordSuccess(TimeSpan elapsed, int itemCount = 1)
    {
        lock (_telemetryLock)
        {
            _llmAttemptCount += itemCount;
            _llmSuccessCount += itemCount;
            _totalLlmDuration += elapsed;
        }
    }

    private void RecordFailure(string failureCode, TimeSpan elapsed, int itemCount = 1)
    {
        lock (_telemetryLock)
        {
            _llmAttemptCount += itemCount;
            _llmFailureCount += itemCount;
            _totalLlmDuration += elapsed;
            _lastFailureCode = failureCode;
        }
    }

    private void RecordBatchCompletion(TimeSpan elapsed, int successCount, int failureCount, string partialFailureCode)
    {
        lock (_telemetryLock)
        {
            _llmAttemptCount += successCount + failureCount;
            _llmSuccessCount += successCount;
            _llmFailureCount += failureCount;
            _totalLlmDuration += elapsed;
            if (failureCount > 0)
            {
                _lastFailureCode = partialFailureCode;
            }
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

    private const string SharedTriagePolicyPrompt = """
        판단 정책:
        1. currentMessage가 이번 발신자의 새 요청입니다. 기본 근거는 currentMessage입니다.
        2. forwardedContext는 현재 발신자가 아래/전달/포워드된 내용을 확인·대응·회신하라고 요구할 때만 근거로 쓰세요.
        3. quotedHistoryPreview만 있는 과거 요청은 stale history이므로 자동 등록하지 마세요.
        4. mailboxRecipientRole이 Cc이면 비일정성 요청은 보통 ignore입니다. 단 회의/참석/일정은 Cc여도 calendarEvent/meeting으로 남기세요.
        5. mailboxRecipientRole이 Direct이고 action/deadline/reply가 있으면 review보다 autoCreateTask를 선호하세요.
        6. 다른 사람에게 명시 배정된 일은 ignore입니다. 명시 대상이 mailboxOwner이면 내 업무입니다. 팀/담당자/전체처럼 불명확하지만 Direct 수신이면 autoCreateTask입니다.
        7. 명확한 action/deadline/reply/meeting이면 autoCreateTask, FYI/공지/감사/단순 확인은 ignore입니다. review는 LLM이 정말 판단 불가할 때만 씁니다.
        8. dueAt은 메일에 근거가 있을 때만 ISO-8601로 쓰고, 없으면 null입니다. 마감일을 상상하지 마세요.
        9. 보낸 사람이 mailboxOwner이면 사용자가 보낸 메일입니다. 사용자가 "제가 보내겠습니다/공유드리겠습니다"처럼 한 약속은 promisedByMe, 사용자가 상대에게 요청하고 기다리는 것은 waitingForReply입니다.
        10. suggestedTitle에는 "메일 확인", "오늘 회신", "D-day", "할 일", "대기" 같은 분류/상태 접두어를 쓰지 마세요.
        11. reason/evidenceSnippet/summary는 UI 보조용이므로 각각 50자 이내로 짧게 쓰세요.

        Few-shot:
        - "영희님 내일까지 비용 자료 검토 후 회신 부탁드립니다" + mailboxOwner "김영희" => autoCreateTask, deadline/replyRequired.
        - "철수님 내일까지 비용 자료 검토 부탁드립니다" + mailboxOwner "김영희" => ignore, explicitAssignee "철수".
        - mailboxRecipientRole "Cc" + "자료 확인 부탁드립니다" => ignore.
        - mailboxRecipientRole "Cc" + "오늘 15시 회의 참석" => autoCreateTask, meeting/calendarEvent.
        - "확인했습니다" + quotedHistoryPreview에 오래된 요청만 있음 => ignore.
        - "아래 고객 요청 건 확인 후 대응 부탁드립니다" + forwardedContext 있음 => currentSenderRequested true, actionOrigin forwardedContext, 명확하면 autoCreateTask 아니면 review.
        - sender가 mailboxOwner + "제가 금요일까지 수정본 공유드리겠습니다" => autoCreateTask, promisedByMe.
        - sender가 mailboxOwner + "20일까지 자료 공유 부탁드립니다" => autoCreateTask, waitingForReply.
        """;

    private static readonly string SystemPrompt = """
        /no_think
        한국어 업무 메일 triage 전용 로컬 비서입니다. 추론 설명 없이 짧은 JSON object 하나만 반환하세요.
        목표: 메일 제목을 요약하지 말고 "사용자가 실제로 해야 할 일"만 30자 이내 suggestedTitle로 만드세요.

        """ + SharedTriagePolicyPrompt + """

        스키마:
        {
          "kind": "none|replyRequired|actionRequested|deadline|promisedByMe|waitingForReply|reviewNeeded|meeting|calendarEvent",
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
        """;

    private static readonly string BatchSystemPrompt = """
        /no_think
        한국어 업무 메일 triage 전용 로컬 비서입니다. items[]를 각각 독립 분석하고 짧은 JSON object 하나만 반환하세요.
        출력은 {"items":[...]} 하나뿐입니다. 각 결과는 입력 id를 그대로 포함하세요. 제목보다 "사용자가 실제로 해야 할 일"을 30자 이내로 쓰세요.

        """ + SharedTriagePolicyPrompt + """

        각 item 스키마:
        {
          "id": "입력 id",
          "kind": "none|replyRequired|actionRequested|deadline|promisedByMe|waitingForReply|reviewNeeded|meeting|calendarEvent",
          "disposition": "ignore|review|autoCreateTask",
          "confidence": 0.0,
          "suggestedTitle": "해야 할 일 30자 이내",
          "reason": "판단 이유 50자 이내",
          "evidenceSnippet": "근거 50자 이내",
          "dueAt": "ISO-8601 또는 null",
          "summary": "요약 50자 이내",
          "actionOrigin": "currentMessage|forwardedContext|quotedHistory|none",
          "currentSenderRequested": true,
          "explicitAssignee": "명시 대상자 또는 null",
          "assignedToMailboxUser": true
        }
        """;

    private sealed record LlmFollowUpResponse(
        string? Id,
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
