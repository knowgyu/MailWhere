using System.Text.Json;

namespace MailWhere.Core.LLM;

public sealed record LlmCallDiagnostics(
    string Provider,
    string Model,
    TimeSpan? TotalDuration = null,
    TimeSpan? LoadDuration = null,
    int? PromptEvalCount = null,
    TimeSpan? PromptEvalDuration = null,
    int? EvalCount = null,
    TimeSpan? EvalDuration = null,
    int? ThinkingCharCount = null)
{
    public string ToCompactKoreanSummary()
    {
        var parts = new List<string>();
        if (TotalDuration is { } total)
        {
            parts.Add($"총 {total.TotalMilliseconds:N0}ms");
        }

        if (LoadDuration is { } load && load > TimeSpan.Zero)
        {
            parts.Add($"로드 {load.TotalMilliseconds:N0}ms");
        }

        if (PromptEvalCount is not null || PromptEvalDuration is not null)
        {
            parts.Add($"입력 {FormatCountAndDuration(PromptEvalCount, PromptEvalDuration)}");
        }

        if (EvalCount is not null || EvalDuration is not null)
        {
            parts.Add($"출력 {FormatCountAndDuration(EvalCount, EvalDuration)}");
        }

        if (ThinkingCharCount is { } thinkingChars)
        {
            parts.Add(thinkingChars > 0 ? $"thinking {thinkingChars:N0}자" : "thinking 없음");
        }

        return parts.Count == 0
            ? $"{Provider} {Model} 메타 없음"
            : $"{Provider} {Model} · {string.Join(" · ", parts)}";
    }

    private static string FormatCountAndDuration(int? count, TimeSpan? duration)
    {
        if (count is { } tokenCount && duration is { } elapsed)
        {
            return $"{tokenCount:N0}tok/{elapsed.TotalMilliseconds:N0}ms";
        }

        if (count is { } onlyCount)
        {
            return $"{onlyCount:N0}tok";
        }

        return duration is { } onlyDuration ? $"{onlyDuration.TotalMilliseconds:N0}ms" : "-";
    }
}

public sealed record LlmCompletion(string Content, LlmCallDiagnostics? Diagnostics = null);

public sealed record LlmRequestOptions(
    int? ContextTokens = null,
    int? MaxOutputTokens = null,
    string? JsonSchemaName = null,
    JsonElement? JsonSchema = null);

public interface ILlmClient
{
    Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null);
}

public sealed class DisabledLlmClient : ILlmClient
{
    public Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null) =>
        throw new InvalidOperationException("LLM provider is disabled. Managed mode disables external providers by default.");
}
