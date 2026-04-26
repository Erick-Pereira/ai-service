namespace Simcag.AIService.Application.Interfaces;

/// <summary>
/// Interface for interacting with Ollama AI service.
/// </summary>
public interface IOllamaClient
{
    Task<string> GenerateCompletionAsync(string prompt, string model = "llama3.1", CancellationToken ct = default);

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Nomes de modelos reportados por <c>GET /api/tags</c> (lista vazia se indisponível).</summary>
    Task<IReadOnlyList<string>> ListInstalledModelNamesAsync(CancellationToken ct = default);
}