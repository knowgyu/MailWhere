namespace OutlookAiSecretary.Core.LLM;

public interface ILlmClient
{
    Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default);
}

public sealed class DisabledLlmClient : ILlmClient
{
    public Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("LLM provider is disabled. Managed mode disables external providers by default.");
}
