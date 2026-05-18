using MailWhere.Core.LLM;

namespace MailWhere.Core.Analysis;

public enum LlmFallbackPolicy
{
    LlmOnly,
    LlmThenRules
}

public sealed record AnalysisTelemetry(
    int LlmAttemptCount,
    int LlmSuccessCount,
    int LlmFallbackCount,
    int LlmFailureCount,
    TimeSpan TotalLlmDuration,
    string? LastFailureCode,
    int LlmRequestCount = 0,
    LlmCallDiagnostics? LastDiagnostics = null)
{
    public static AnalysisTelemetry Empty { get; } = new(0, 0, 0, 0, TimeSpan.Zero, null, 0, null);

    public string ToKoreanSummary()
    {
        if (LlmAttemptCount == 0 && LlmRequestCount == 0)
        {
            return "LLM 분석 없음";
        }

        var requestCount = Math.Max(1, LlmRequestCount);
        var requestAverageMs = Math.Round(TotalLlmDuration.TotalMilliseconds / requestCount);
        var itemAverageMs = LlmAttemptCount > 0
            ? $" · 항목 환산 {Math.Round(TotalLlmDuration.TotalMilliseconds / LlmAttemptCount):N0}ms"
            : string.Empty;
        var failureText = string.IsNullOrWhiteSpace(LastFailureCode) ? string.Empty : $" · 최근 실패 {LastFailureCode}";
        var diagnosticsText = LastDiagnostics is null ? string.Empty : $" · 최근 {LastDiagnostics.ToCompactKoreanSummary()}";
        return $"LLM 요청 {LlmRequestCount}회 · 항목 {LlmAttemptCount}건 · 성공 {LlmSuccessCount}건 · fallback {LlmFallbackCount}건 · 실패 {LlmFailureCount}건 · 요청 평균 {requestAverageMs:N0}ms{itemAverageMs}{failureText}{diagnosticsText}";
    }
}

public interface IAnalysisTelemetrySource
{
    AnalysisTelemetry GetTelemetrySnapshot();
}
