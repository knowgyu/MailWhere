using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailWhere.Core.LLM;

public abstract class HttpJsonLlmClient : ILlmClient
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected readonly HttpClient HttpClient;
    protected readonly LlmEndpointSettings Settings;

    protected HttpJsonLlmClient(HttpClient httpClient, LlmEndpointSettings settings)
    {
        HttpClient = httpClient;
        Settings = settings;
        HttpClient.Timeout = settings.Timeout;
    }

    public abstract Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null);

    internal static Uri BuildUri(string endpoint, string suffix)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (suffix.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed + suffix[3..], UriKind.Absolute);
        }

        return new Uri(trimmed + suffix, UriKind.Absolute);
    }

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    protected static object BuildChatResponseFormat(LlmRequestOptions? requestOptions)
    {
        if (requestOptions?.StructuredOutputMode == LlmStructuredOutputMode.JsonObject
            || requestOptions?.JsonSchema is not { } schema
            || string.IsNullOrWhiteSpace(requestOptions.JsonSchemaName))
        {
            return new { type = "json_object" };
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = requestOptions.JsonSchemaName,
                strict = true,
                schema
            }
        };
    }

    protected static object BuildResponsesTextFormat(LlmRequestOptions? requestOptions)
    {
        if (requestOptions?.StructuredOutputMode == LlmStructuredOutputMode.JsonObject
            || requestOptions?.JsonSchema is not { } schema
            || string.IsNullOrWhiteSpace(requestOptions.JsonSchemaName))
        {
            return new { type = "json_object" };
        }

        return new
        {
            type = "json_schema",
            name = requestOptions.JsonSchemaName,
            strict = true,
            schema
        };
    }

    protected static bool ShouldDisableThinkingWithTemplate(LlmRequestOptions? requestOptions) =>
        requestOptions?.ThinkingControlMode == LlmThinkingControlMode.EnableThinkingFalse;

    protected static bool ShouldDisableThinkingWithReasoningEffort(LlmRequestOptions? requestOptions) =>
        requestOptions?.ThinkingControlMode == LlmThinkingControlMode.ReasoningEffortNone;

    protected static double TemperatureOrDefault(LlmRequestOptions? requestOptions) =>
        requestOptions?.Temperature is { } temperature ? temperature : 0.1;
}

public sealed class OllamaLlmClient : HttpJsonLlmClient
{
    public OllamaLlmClient(HttpClient httpClient, LlmEndpointSettings settings) : base(httpClient, settings)
    {
    }

    public override async Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null)
    {
        if (!Settings.CanCall)
        {
            throw new InvalidOperationException("LLM 설정이 비활성화되어 있습니다.");
        }

        var body = new
        {
            model = Settings.Model,
            stream = false,
            think = false,
            format = "json",
            options = new
            {
                temperature = TemperatureOrDefault(requestOptions),
                num_ctx = requestOptions?.ContextTokens,
                num_predict = requestOptions?.MaxOutputTokens ?? 1280,
                top_p = 0.9
            },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/api/chat"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var content = json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return new LlmCompletion(content, ExtractOllamaDiagnostics(json.RootElement, Settings.Model));
    }

    private static LlmCallDiagnostics ExtractOllamaDiagnostics(JsonElement root, string model)
    {
        var thinkingCharCount = TryGetThinkingCharCount(root);
        return new LlmCallDiagnostics(
            "Ollama",
            model,
            TotalDuration: TryGetNanosecondDuration(root, "total_duration"),
            LoadDuration: TryGetNanosecondDuration(root, "load_duration"),
            PromptEvalCount: TryGetInt(root, "prompt_eval_count"),
            PromptEvalDuration: TryGetNanosecondDuration(root, "prompt_eval_duration"),
            EvalCount: TryGetInt(root, "eval_count"),
            EvalDuration: TryGetNanosecondDuration(root, "eval_duration"),
            ThinkingCharCount: thinkingCharCount);
    }

    private static int? TryGetThinkingCharCount(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("thinking", out var thinking)
            || thinking.ValueKind != JsonValueKind.String)
        {
            return 0;
        }

        return thinking.GetString()?.Length ?? 0;
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static TimeSpan? TryGetNanosecondDuration(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt64(out var nanoseconds)
            || nanoseconds < 0)
        {
            return null;
        }

        return TimeSpan.FromTicks(nanoseconds / 100);
    }
}

public sealed class OpenAiChatCompletionsLlmClient : HttpJsonLlmClient
{
    public OpenAiChatCompletionsLlmClient(HttpClient httpClient, LlmEndpointSettings settings) : base(httpClient, settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }
    }

    public override async Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null)
    {
        if (!Settings.CanCall)
        {
            throw new InvalidOperationException("LLM 설정이 비활성화되어 있습니다.");
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = Settings.Model,
            ["temperature"] = TemperatureOrDefault(requestOptions),
            ["max_tokens"] = requestOptions?.MaxOutputTokens ?? 1280,
            ["response_format"] = BuildChatResponseFormat(requestOptions),
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };
        if (ShouldDisableThinkingWithReasoningEffort(requestOptions))
        {
            body["reasoning_effort"] = "none";
        }
        if (ShouldDisableThinkingWithTemplate(requestOptions))
        {
            body["chat_template_kwargs"] = new { enable_thinking = false };
        }

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/chat/completions"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var choice = json.RootElement.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason)
            && finishReason.ValueKind == JsonValueKind.String
            && string.Equals(finishReason.GetString(), "length", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("LLM response was truncated.");
        }

        return new LlmCompletion(
            choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty,
            OpenAiDiagnosticsExtractor.Extract(json.RootElement, Settings.Provider.ToString(), Settings.Model));
    }
}

public sealed class OpenAiResponsesLlmClient : HttpJsonLlmClient
{
    public OpenAiResponsesLlmClient(HttpClient httpClient, LlmEndpointSettings settings) : base(httpClient, settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }
    }

    public override async Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null)
    {
        if (!Settings.CanCall)
        {
            throw new InvalidOperationException("LLM 설정이 비활성화되어 있습니다.");
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = Settings.Model,
            ["store"] = false,
            ["temperature"] = TemperatureOrDefault(requestOptions),
            ["max_output_tokens"] = requestOptions?.MaxOutputTokens ?? 1280,
            ["text"] = new { format = BuildResponsesTextFormat(requestOptions) },
            ["input"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };
        if (ShouldDisableThinkingWithReasoningEffort(requestOptions))
        {
            body["reasoning"] = new { effort = "none" };
        }
        if (ShouldDisableThinkingWithTemplate(requestOptions))
        {
            body["chat_template_kwargs"] = new { enable_thinking = false };
        }

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/responses"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        ThrowIfIncomplete(json.RootElement);
        return new LlmCompletion(
            ExtractOutputText(json.RootElement),
            OpenAiDiagnosticsExtractor.Extract(json.RootElement, Settings.Provider.ToString(), Settings.Model));
    }

    private static void ThrowIfIncomplete(JsonElement root)
    {
        if (HasStringValue(root, "status", "incomplete")
            || HasStringValue(root, "finish_reason", "length")
            || HasStringValue(root, "reason", "max_output_tokens"))
        {
            throw new JsonException("LLM response was truncated.");
        }
    }

    private static bool HasStringValue(JsonElement element, string propertyName, string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Value.GetString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (HasStringValue(property.Value, propertyName, value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasStringValue(item, propertyName, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }
}

internal static class OpenAiDiagnosticsExtractor
{
    public static LlmCallDiagnostics Extract(JsonElement root, string provider, string model) =>
        new(provider, model, ThinkingCharCount: CountReasoningChars(root));

    private static int CountReasoningChars(JsonElement element, string? propertyName = null, bool reasoningContext = false)
    {
        var isReasoningField = propertyName is not null
            && (propertyName.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("thinking", StringComparison.OrdinalIgnoreCase));
        var inReasoning = reasoningContext || isReasoningField || IsReasoningBlock(element);

        return element.ValueKind switch
        {
            JsonValueKind.String => inReasoning ? element.GetString()?.Length ?? 0 : 0,
            JsonValueKind.Object => element.EnumerateObject().Sum(property => CountReasoningChars(property.Value, property.Name, inReasoning)),
            JsonValueKind.Array => element.EnumerateArray().Sum(item => CountReasoningChars(item, propertyName, inReasoning)),
            _ => 0
        };
    }

    private static bool IsReasoningBlock(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var typeName = type.GetString();
        return typeName?.Contains("reasoning", StringComparison.OrdinalIgnoreCase) == true
            || typeName?.Contains("thinking", StringComparison.OrdinalIgnoreCase) == true;
    }
}

public static class LlmClientFactory
{
    public static ILlmClient Create(LlmEndpointSettings settings, HttpClient? httpClient = null)
    {
        if (!settings.CanCall)
        {
            return new DisabledLlmClient();
        }

        var client = httpClient ?? new HttpClient();
        return settings.Provider switch
        {
            LlmProviderKind.OllamaNative => new OllamaLlmClient(client, settings),
            LlmProviderKind.OpenAiChatCompletions => new OpenAiChatCompletionsLlmClient(client, settings),
            LlmProviderKind.OpenAiResponses => new OpenAiResponsesLlmClient(client, settings),
            _ => new DisabledLlmClient()
        };
    }
}
