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
    string? LastFailureCode)
{
    public static AnalysisTelemetry Empty { get; } = new(0, 0, 0, 0, TimeSpan.Zero, null);

    public string ToKoreanSummary()
    {
        if (LlmAttemptCount == 0)
        {
            return "LLM 분석 없음";
        }

        var averageMs = Math.Round(TotalLlmDuration.TotalMilliseconds / Math.Max(1, LlmAttemptCount));
        var failureText = string.IsNullOrWhiteSpace(LastFailureCode) ? string.Empty : $" · 최근 실패 {LastFailureCode}";
        return $"LLM 시도 {LlmAttemptCount}건 · 성공 {LlmSuccessCount}건 · fallback {LlmFallbackCount}건 · 실패 {LlmFailureCount}건 · 평균 {averageMs:N0}ms{failureText}";
    }
}

public interface IAnalysisTelemetrySource
{
    AnalysisTelemetry GetTelemetrySnapshot();
}
