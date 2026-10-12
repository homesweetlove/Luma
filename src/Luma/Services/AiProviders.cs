namespace Luma.Services;

public sealed record AiRequest(string Prompt, string? PageUrl = null, string? PageSnapshot = null);
public sealed record AiResponse(string Text, string Provider, string? Model = null);

public interface IAiProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}

public interface IExternalAiDelegate
{
    string Name { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// Opens or uses a visible external AI surface and delegates a bounded task.
    /// Implementations must not expose vault secrets in prompts.
    /// </summary>
    Task<string> DelegateVisibleAsync(string task, string? context, CancellationToken cancellationToken = default);
}

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IAiProvider> Providers => _providers.Values;

    public void Register(IAiProvider provider) => _providers[provider.Name] = provider;

    public IAiProvider? TryGet(string name) => _providers.GetValueOrDefault(name);
}
