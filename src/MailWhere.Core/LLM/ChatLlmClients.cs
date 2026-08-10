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
        if (requestOptions?.JsonSchema is not { } schema
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
        if (requestOptions?.JsonSchema is not { } schema
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
                temperature = 0.1,
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

        var body = new
        {
            model = Settings.Model,
            temperature = 0.1,
            max_tokens = requestOptions?.MaxOutputTokens ?? 1280,
            response_format = BuildChatResponseFormat(requestOptions),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/chat/completions"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return new LlmCompletion(
            json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty);
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

        var body = new
        {
            model = Settings.Model,
            store = false,
            temperature = 0.1,
            max_output_tokens = requestOptions?.MaxOutputTokens ?? 1280,
            text = new
            {
                format = BuildResponsesTextFormat(requestOptions)
            },
            input = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/responses"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return new LlmCompletion(ExtractOutputText(json.RootElement));
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
