using System.Net.Http.Headers;
using System.Text.Json;

namespace MailWhere.Core.LLM;

public static class LlmModelCatalog
{
    public static async Task<IReadOnlyList<string>> FetchAsync(
        LlmEndpointSettings settings,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (settings.Provider == LlmProviderKind.Disabled || string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("모델 목록을 불러오려면 provider와 endpoint가 필요합니다.");
        }

        var client = httpClient ?? new HttpClient();
        client.Timeout = settings.Timeout;
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        return settings.Provider switch
        {
            LlmProviderKind.OllamaNative => await FetchOllamaModelsAsync(client, settings.Endpoint, cancellationToken).ConfigureAwait(false),
            LlmProviderKind.OpenAiChatCompletions or LlmProviderKind.OpenAiResponses => await FetchOpenAiCompatibleModelsAsync(client, settings.Endpoint, cancellationToken).ConfigureAwait(false),
            _ => Array.Empty<string>()
        };
    }

    private static async Task<IReadOnlyList<string>> FetchOllamaModelsAsync(HttpClient client, string endpoint, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(HttpJsonLlmClient.BuildUri(endpoint, "/api/tags"), cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return models.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> FetchOpenAiCompatibleModelsAsync(HttpClient client, string endpoint, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(HttpJsonLlmClient.BuildUri(endpoint, "/v1/models"), cancellationToken).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
