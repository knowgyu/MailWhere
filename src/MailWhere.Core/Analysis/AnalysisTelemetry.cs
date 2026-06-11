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
        var failureText = string.IsNullOrWhiteSpace(LastFailureCode) ? string.Empty : $" · 최근 실패 {LastFailureCode}({DescribeFailureCode(LastFailureCode)})";
        var diagnosticsText = LastDiagnostics is null ? string.Empty : $" · 최근 {LastDiagnostics.ToCompactKoreanSummary()}";
        return $"LLM 요청 {LlmRequestCount}회 · 항목 {LlmAttemptCount}건 · 성공 {LlmSuccessCount}건 · fallback {LlmFallbackCount}건 · 실패 {LlmFailureCount}건 · 요청 평균 {requestAverageMs:N0}ms{itemAverageMs}{failureText}{diagnosticsText}";
    }

    private static string DescribeFailureCode(string failureCode) =>
        failureCode switch
        {
            "timeout" => "응답 시간 초과",
            "invalid-json" => "응답 형식 오류",
            "empty-response" => "빈 응답",
            "invalid-settings" => "설정 오류",
            "missing-batch-item" => "배치 응답 누락",
            "partial-batch" => "일부 배치 누락",
            _ when failureCode.StartsWith("http-429", StringComparison.OrdinalIgnoreCase) => "요청 한도/동시 요청 과다",
            _ when failureCode.StartsWith("http-5", StringComparison.OrdinalIgnoreCase) => "서버 오류",
            _ when failureCode.StartsWith("http-", StringComparison.OrdinalIgnoreCase) => "HTTP 오류",
            _ => failureCode
        };
}

public interface IAnalysisTelemetrySource
{
    AnalysisTelemetry GetTelemetrySnapshot();
}
