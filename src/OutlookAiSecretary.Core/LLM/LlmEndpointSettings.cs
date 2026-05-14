namespace OutlookAiSecretary.Core.LLM;

public enum LlmProviderKind
{
    Disabled,
    Ollama,
    OpenAiCompatible
}

public sealed record LlmEndpointSettings(
    LlmProviderKind Provider,
    bool Enabled,
    string Endpoint,
    string Model,
    string? ApiKey,
    int TimeoutSeconds)
{
    public static LlmEndpointSettings Disabled { get; } = new(
        LlmProviderKind.Disabled,
        Enabled: false,
        Endpoint: string.Empty,
        Model: string.Empty,
        ApiKey: null,
        TimeoutSeconds: 30);

    public bool CanCall => Enabled && Provider != LlmProviderKind.Disabled && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Model);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 180));
}
