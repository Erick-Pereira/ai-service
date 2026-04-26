using Simcag.AIService.Application.Exceptions;
using Simcag.AIService.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Simcag.AIService.Infrastructure.Clients;

/// <summary>
/// Client HTTP para integração com Ollama API.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<OllamaClient> _logger;
    private readonly ConcurrentDictionary<string, string> _resolvedModelByRequest = new(StringComparer.OrdinalIgnoreCase);

    public OllamaClient(HttpClient httpClient, ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Env-first configuration (no appsettings). Keep backward compatibility with OLLAMA_BASE_URL.
        // Preferred: OLLAMA_HOST (ex.: http://localhost:11434)
        var rawUrl =
            Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? "http://localhost:11434";
        _baseUrl = rawUrl.TrimEnd('/');

        // Configura BaseAddress corretamente
        _httpClient.BaseAddress = new Uri(_baseUrl);

        _logger.LogInformation("OllamaClient configured with BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
    }

    /// <summary>
    /// Generates a completion from Ollama API. Note: This method may throw <see cref="AiServiceException"/>.
    /// for various error scenarios (network errors, timeouts, server errors, etc.).
    /// Callers should implement appropriate error handling and fallback strategies.
    /// </summary>
    public async Task<string> GenerateCompletionAsync(string prompt, string model = "llama3.1", CancellationToken ct = default)
    {
        var effectiveModel = await ResolveEffectiveModelNameAsync(model, ct);
        try
        {
            var request = new OllamaRequest(effectiveModel, prompt, false);
            var response = await _httpClient.PostAsJsonAsync("api/generate", request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Ollama POST api/generate failed: status {Status} model {EffectiveModel} (requested {RequestedModel}). Body: {Body}",
                    (int)response.StatusCode,
                    effectiveModel,
                    model,
                    raw);

                if ((int)response.StatusCode == 404)
                    _resolvedModelByRequest.TryRemove(NormalizeModelKey(model), out _);

                response.EnsureSuccessStatusCode();
            }

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<OllamaResponse>(raw, jsonOptions);
            return result?.Response ?? string.Empty;
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500)
        {
            // Server error from Ollama (body já logado acima quando a resposta não foi sucesso)
            _logger.LogError(ex, "Ollama server error for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service encountered an error", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value == 404)
        {
            // Allow retry after modelo instalado / tags alteradas
            _resolvedModelByRequest.TryRemove(NormalizeModelKey(model), out _);
            // Model not found or wrong endpoint
            _logger.LogError(ex, "Ollama endpoint or model not found: effective {EffectiveModel}, requested {RequestedModel}", effectiveModel, model);
            throw new AiServiceException("AI model not found", ex);
        }
        catch (HttpRequestException ex)
        {
            // Network or connectivity issues
            _logger.LogError(ex, "Network error connecting to Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("Unable to connect to AI service", ex);
        }
        catch (TaskCanceledException ex)
        {
            // Timeout
            _logger.LogError(ex, "Request to Ollama timed out for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service request timed out", ex);
        }
        catch (JsonException ex)
        {
            // Deserialization error
            _logger.LogError(ex, "Invalid response format from Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("Received invalid response from AI service", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating completion from Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service is currently unavailable", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama health check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInstalledModelNamesAsync(CancellationToken ct = default)
    {
        try
        {
            var tagsResponse = await _httpClient.GetAsync("api/tags", ct);
            if (!tagsResponse.IsSuccessStatusCode)
                return Array.Empty<string>();

            await using var stream = await tagsResponse.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return ParseModelNames(doc);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list Ollama models");
            return Array.Empty<string>();
        }
    }

    // Note: There's a potential TOCTOU (Time-of-check Time-of-use) race condition
    // between IsAvailableAsync and GenerateCompletionAsync. Even if IsAvailableAsync
    // returns true, the Ollama service could become unavailable before GenerateCompletionAsync
    // is called. Callers should handle exceptions from GenerateCompletionAsync gracefully
    // and implement fallback strategies, as done in ExpenseClassificationService and 
    // SupplierExtractionService.
    private record OllamaRequest(string Model, string Prompt, bool Stream);
    private record OllamaResponse(string Response);

    /// <summary>
    /// Ollama lista modelos com tag completa (ex.: <c>llama3.1:latest</c>). <c>MODEL_NAME=llama3.1</c> costuma 404 até existir correspondência exata.
    /// Resolve via <c>GET /api/tags</c>: match exato, depois <c>{base}:latest</c>, depois qualquer tag com o mesmo prefixo antes de <c>:</c>.
    /// </summary>
    private static string NormalizeModelKey(string? requested) =>
        string.IsNullOrWhiteSpace(requested) ? "llama3.1" : requested.Trim();

    private async Task<string> ResolveEffectiveModelNameAsync(string requested, CancellationToken ct)
    {
        var key = NormalizeModelKey(requested);
        if (_resolvedModelByRequest.TryGetValue(key, out var memo))
            return memo;

        List<string> names;
        try
        {
            var tagsResponse = await _httpClient.GetAsync("api/tags", ct);
            if (!tagsResponse.IsSuccessStatusCode)
            {
                _resolvedModelByRequest[key] = key;
                return key;
            }

            await using var stream = await tagsResponse.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            names = ParseModelNames(doc);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list Ollama models; using requested model name as-is");
            _resolvedModelByRequest[key] = key;
            return key;
        }

        if (names.Count == 0)
        {
            _resolvedModelByRequest[key] = key;
            return key;
        }

        static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        string? resolved = names.FirstOrDefault(n => Eq(n, key));

        // Base sem tag (ex.: llama3.1): preferir explicitamente :latest entre variantes llama3.1:* (evita pegar :8b só por ordem na API).
        if (resolved == null && !key.Contains(':', StringComparison.Ordinal))
        {
            var variants = names
                .Where(n => n.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (variants.Count > 0)
            {
                resolved = variants.FirstOrDefault(v => v.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
                    ?? variants.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).First();
            }
        }

        if (resolved == null)
        {
            var baseName = key.Contains(':', StringComparison.Ordinal) ? key.Split(':')[0] : key;
            resolved = names.FirstOrDefault(n =>
            {
                var nb = n.Contains(':', StringComparison.Ordinal) ? n.Split(':')[0] : n;
                return Eq(nb, baseName);
            });
        }

        if (resolved == null && names.Count == 1)
        {
            _logger.LogWarning(
                "MODEL_NAME '{Requested}' not found in local Ollama models; using the only installed model '{Resolved}'",
                key,
                names[0]);
            resolved = names[0];
        }

        if (resolved == null)
        {
            _logger.LogWarning(
                "MODEL_NAME '{Requested}' not found in Ollama. Installed models: {Models}. Set MODEL_NAME to one of these names.",
                key,
                string.Join(", ", names));
            _resolvedModelByRequest[key] = key;
            return key;
        }

        if (!Eq(resolved, key))
        {
            _logger.LogInformation("Ollama model name resolved: {Requested} -> {Resolved}", key, resolved);
        }

        _resolvedModelByRequest[key] = resolved;
        return resolved;
    }

    private static List<string> ParseModelNames(JsonDocument doc)
    {
        var list = new List<string>();
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var m in models.EnumerateArray())
        {
            if (m.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var s = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
        }

        return list;
    }
}
