using System.Diagnostics;
using System.Text.Json;

namespace MailWhere.Core.LLM;

public sealed record LlmEndpointProbeResult(
    bool Success,
    string Code,
    TimeSpan Duration,
    string Provider,
    string Model)
{
    public string ToKoreanStatus()
    {
        var elapsed = $"{Math.Round(Duration.TotalMilliseconds):N0}ms";
        return Success
            ? $"LLM 연결 성공 · {Provider}/{Model} · {elapsed}"
            : $"LLM 연결 실패 · {Code} · {Provider}/{Model} · {elapsed}";
    }
}

public static class LlmEndpointProbe
{
    private const string ProbePrompt = """
        메일 내용이 아닌 연결 테스트입니다.
        반드시 {"ok":true,"source":"mailwhere-probe"} 형태의 JSON object 하나만 반환하세요.
        """;

    public static async Task<LlmEndpointProbeResult> ProbeAsync(
        LlmEndpointSettings settings,
        ILlmClient? client = null,
        CancellationToken cancellationToken = default)
    {
        var provider = settings.Provider.ToString();
        var model = string.IsNullOrWhiteSpace(settings.Model) ? "(model-empty)" : settings.Model;
        var stopwatch = Stopwatch.StartNew();

        if (!settings.CanCall)
        {
            return new LlmEndpointProbeResult(false, "not-configured", stopwatch.Elapsed, provider, model);
        }

        try
        {
            var llmClient = client ?? LlmClientFactory.Create(settings);
            var raw = await llmClient.CompleteJsonAsync(
                "You are a JSON connectivity probe. Return one JSON object only.",
                ProbePrompt,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            using var json = JsonDocument.Parse(raw);
            return json.RootElement.ValueKind == JsonValueKind.Object
                ? new LlmEndpointProbeResult(true, "ok", stopwatch.Elapsed, provider, model)
                : new LlmEndpointProbeResult(false, "non-object-json", stopwatch.Elapsed, provider, model);
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return new LlmEndpointProbeResult(false, "invalid-json", stopwatch.Elapsed, provider, model);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            return new LlmEndpointProbeResult(false, "timeout", stopwatch.Elapsed, provider, model);
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            return new LlmEndpointProbeResult(false, "http-error", stopwatch.Elapsed, provider, model);
        }
        catch (InvalidOperationException)
        {
            stopwatch.Stop();
            return new LlmEndpointProbeResult(false, "invalid-settings", stopwatch.Elapsed, provider, model);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new LlmEndpointProbeResult(false, ex.GetType().Name, stopwatch.Elapsed, provider, model);
        }
    }
}
