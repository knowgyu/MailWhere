using System.Diagnostics;
using System.Text.Json;
using MailWhere.Core.Analysis;
using MailWhere.Core.Capabilities;

namespace MailWhere.Core.LLM;

public sealed record LlmEndpointProbeResult(
    bool Success,
    string Code,
    TimeSpan Duration,
    string Provider,
    string Model,
    LlmProbeProof? Proof = null)
{
    public string ToKoreanStatus()
    {
        var elapsed = $"{Math.Round(Duration.TotalMilliseconds):N0}ms";
        var selected = Proof is null ? string.Empty : $" · thinking {Proof.SelectedThinkingControlMode}";
        return Success
            ? $"LLM 연결 성공 · {Provider}/{Model}{selected} · 분석 형식 확인 · {elapsed}"
            : $"LLM 연결 실패 · {Code} · {Provider}/{Model} · {elapsed}";
    }
}

public static class LlmEndpointProbe
{
    public static Task<LlmEndpointProbeResult> ProbeAsync(
        RuntimeSettings settings,
        ILlmClient? client = null,
        CancellationToken cancellationToken = default) =>
        ProbeAsync(settings.ToLlmEndpointSettings(), settings.ToConfiguredLlmAnalysisSettings(), settings, client, cancellationToken);

    public static Task<LlmEndpointProbeResult> ProbeAsync(
        LlmEndpointSettings settings,
        ILlmClient? client = null,
        CancellationToken cancellationToken = default) =>
        ProbeAsync(settings, LlmAnalysisSettings.Default, runtimeSettings: null, client, cancellationToken);

    private static async Task<LlmEndpointProbeResult> ProbeAsync(
        LlmEndpointSettings settings,
        LlmAnalysisSettings analysisSettings,
        RuntimeSettings? runtimeSettings,
        ILlmClient? client,
        CancellationToken cancellationToken)
    {
        var provider = settings.Provider.ToString();
        var model = string.IsNullOrWhiteSpace(settings.Model) ? "(model-empty)" : settings.Model;
        var stopwatch = Stopwatch.StartNew();

        if (!settings.CanCall)
        {
            return new LlmEndpointProbeResult(false, "not-configured", stopwatch.Elapsed, provider, model);
        }

        var llmClient = client ?? LlmClientFactory.Create(settings);
        var modes = analysisSettings.ThinkingControlMode == LlmThinkingControlMode.Auto
            ? new[] { LlmThinkingControlMode.EnableThinkingFalse, LlmThinkingControlMode.ReasoningEffortNone }
            : new[] { analysisSettings.ThinkingControlMode };
        var lastCode = "analysis-shape";

        foreach (var mode in modes)
        {
            var code = await TryProbeModeAsync(
                llmClient,
                analysisSettings with { ThinkingControlMode = mode },
                cancellationToken).ConfigureAwait(false);
            if (code is null)
            {
                stopwatch.Stop();
                return new LlmEndpointProbeResult(
                    true,
                    "ok",
                    stopwatch.Elapsed,
                    provider,
                    model,
                    runtimeSettings?.WithSuccessfulLlmProbeProof(DateTimeOffset.UtcNow, mode).LastSuccessfulLlmProbeProof);
            }

            lastCode = code;
        }

        stopwatch.Stop();
        return new LlmEndpointProbeResult(false, lastCode, stopwatch.Elapsed, provider, model);
    }

    private static bool HasThinkingDiagnostics(LlmCompletion completion) =>
        completion.Diagnostics?.ThinkingCharCount is > 0;

    private static async Task<string?> TryProbeModeAsync(
        ILlmClient llmClient,
        LlmAnalysisSettings analysisSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            var single = await llmClient.CompleteJsonAsync(
                "You are the MailWhere analyzer probe. Return JSON only.",
                LlmBackedFollowUpAnalyzer.BuildProbeSinglePayload(),
                cancellationToken,
                LlmBackedFollowUpAnalyzer.BuildRequestOptions(1, isBatch: false, analysisSettings)).ConfigureAwait(false);
            if (HasThinkingDiagnostics(single) || !LlmBackedFollowUpAnalyzer.AcceptsProbeSingleResponse(single.Content))
            {
                return "single-analysis-shape";
            }

            var batch = await llmClient.CompleteJsonAsync(
                "You are the MailWhere batch analyzer probe. Return JSON only.",
                LlmBackedFollowUpAnalyzer.BuildProbeBatchPayload(),
                cancellationToken,
                LlmBackedFollowUpAnalyzer.BuildRequestOptions(2, isBatch: true, analysisSettings)).ConfigureAwait(false);
            if (HasThinkingDiagnostics(batch) || !LlmBackedFollowUpAnalyzer.AcceptsProbeBatchResponse(batch.Content))
            {
                return "batch-analysis-shape";
            }

            var closure = await llmClient.CompleteJsonAsync(
                "You are the MailWhere waiting-closure probe. Return JSON only.",
                LlmBackedWaitingClosureJudge.BuildProbePayload(),
                cancellationToken,
                LlmBackedWaitingClosureJudge.BuildRequestOptions(analysisSettings)).ConfigureAwait(false);
            return !HasThinkingDiagnostics(closure) && LlmBackedWaitingClosureJudge.AcceptsProbeResponse(closure.Content)
                ? null
                : "closure-analysis-shape";
        }
        catch (JsonException)
        {
            return "invalid-json";
        }
        catch (TaskCanceledException)
        {
            return "timeout";
        }
        catch (HttpRequestException)
        {
            return "http-error";
        }
        catch (InvalidOperationException)
        {
            return "invalid-settings";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }
}
