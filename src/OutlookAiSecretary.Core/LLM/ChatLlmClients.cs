using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutlookAiSecretary.Core.LLM;

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

    protected static Uri BuildUri(string endpoint, string suffix)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed, UriKind.Absolute);
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

public sealed class OpenAiCompatibleLlmClient : HttpJsonLlmClient
{
    public OpenAiCompatibleLlmClient(HttpClient httpClient, LlmEndpointSettings settings) : base(httpClient, settings)
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
            LlmProviderKind.Ollama => new OllamaLlmClient(client, settings),
            LlmProviderKind.OpenAiCompatible => new OpenAiCompatibleLlmClient(client, settings),
            _ => new DisabledLlmClient()
        };
    }
}
