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

    public abstract Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default);

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
}

public sealed class OllamaLlmClient : HttpJsonLlmClient
{
    public OllamaLlmClient(HttpClient httpClient, LlmEndpointSettings settings) : base(httpClient, settings)
    {
    }

    public override async Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default)
    {
        if (!Settings.CanCall)
        {
            throw new InvalidOperationException("LLM 설정이 비활성화되어 있습니다.");
        }

        var body = new
        {
            model = Settings.Model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1 },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/api/chat"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
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

    public override async Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default)
    {
        if (!Settings.CanCall)
        {
            throw new InvalidOperationException("LLM 설정이 비활성화되어 있습니다.");
        }

        var body = new
        {
            model = Settings.Model,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/chat/completions"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
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

    public override async Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default)
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
            text = new
            {
                format = new
                {
                    type = "json_object"
                }
            },
            input = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(BuildUri(Settings.Endpoint, "/v1/responses"), body, JsonOptions, cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ExtractOutputText(json.RootElement);
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
