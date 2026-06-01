using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Simcag.AIService.Application.Exceptions;
using Simcag.Shared.Telemetry;

namespace Simcag.AIService.Infrastructure.Clients;

/// <summary>
/// Cliente HTTP direto ao Ollama (sem fila/retries). Usado apenas pelo coordenador de inferência.
/// </summary>
public sealed class OllamaHttpClient
{
    public const string HttpClientName = "ollama";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaHttpClient> _logger;
    private readonly ConcurrentDictionary<string, string> _resolvedModelByRequest = new(StringComparer.OrdinalIgnoreCase);

    public OllamaHttpClient(IHttpClientFactory httpClientFactory, ILogger<OllamaHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OllamaGenerationOutcome> GenerateCompletionRawAsync(
        string prompt,
        string model,
        CancellationToken ct)
    {
        var effectiveModel = await ResolveEffectiveModelNameAsync(model, ct).ConfigureAwait(false);
        using var activity = SimcagActivitySources.AI.StartActivity("ollama.http.generate", ActivityKind.Client);
        activity?.SetTag("ai.model.requested", model);
        activity?.SetTag("ai.model.effective", effectiveModel);

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            var request = new OllamaRequest(effectiveModel, prompt, false);
            var response = await http.PostAsJsonAsync("api/generate", request, ct).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var text = root.TryGetProperty("response", out var respEl) && respEl.ValueKind == JsonValueKind.String
                ? respEl.GetString() ?? string.Empty
                : string.Empty;

            int? promptEval = null;
            if (root.TryGetProperty("prompt_eval_count", out var pe) && pe.ValueKind == JsonValueKind.Number && pe.TryGetInt32(out var pi))
                promptEval = pi;

            int? evalCount = null;
            if (root.TryGetProperty("eval_count", out var ev) && ev.ValueKind == JsonValueKind.Number && ev.TryGetInt32(out var ei))
                evalCount = ei;

            activity?.SetStatus(ActivityStatusCode.Ok);
            return new OllamaGenerationOutcome(text, promptEval, evalCount);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Ollama server error for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException($"AI service encountered an error: HTTP {(int)ex.StatusCode.Value}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value == 404)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _resolvedModelByRequest.TryRemove(NormalizeModelKey(model), out _);
            _logger.LogError(ex, "Ollama endpoint or model not found: effective {EffectiveModel}, requested {RequestedModel}", effectiveModel, model);
            throw new AiServiceException("AI model not found in registry");
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Network error connecting to Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service is unreachable or network connection failed");
        }
        catch (TaskCanceledException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "timeout");
            _logger.LogError(ex, "Request to Ollama timed out for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service request timed out", ex);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Invalid response format from Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("Received invalid response from AI service", ex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Unexpected error generating completion from Ollama for model {EffectiveModel} (requested {RequestedModel})", effectiveModel, model);
            throw new AiServiceException("AI service is currently unavailable", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            var response = await http.GetAsync("api/tags", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama health check failed");
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListInstalledModelNamesAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            var tagsResponse = await http.GetAsync("api/tags", ct).ConfigureAwait(false);
            if (!tagsResponse.IsSuccessStatusCode)
                return Array.Empty<string>();

            await using var stream = await tagsResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return ParseModelNames(doc);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list Ollama models");
            return Array.Empty<string>();
        }
    }

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
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            var tagsResponse = await http.GetAsync("api/tags", ct).ConfigureAwait(false);
            if (!tagsResponse.IsSuccessStatusCode)
            {
                _resolvedModelByRequest[key] = key;
                return key;
            }

            await using var stream = await tagsResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
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

        if (resolved == null && names.Count > 0)
        {
            resolved = names.FirstOrDefault(n => n.StartsWith("llama3.1:", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => n.StartsWith("llama", StringComparison.OrdinalIgnoreCase))
                ?? names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First();
            _logger.LogWarning(
                "MODEL_NAME '{Requested}' não encontrado no Ollama; usando fallback '{Resolved}'. Modelos instalados: {Models}. Ajuste MODEL_NAME para uma tag listada em GET /api/tags.",
                key,
                resolved,
                string.Join(", ", names));
        }

        if (resolved == null)
        {
            _logger.LogWarning(
                "MODEL_NAME '{Requested}' not found in Ollama and no models listed. Set MODEL_NAME or install a model.",
                key);
            _resolvedModelByRequest[key] = key;
            return key;
        }

        if (!Eq(resolved, key))
        {
            SimcagMeters.AiModelFallbacks.Add(1,
                new KeyValuePair<string, object?>("requested", key),
                new KeyValuePair<string, object?>("effective", resolved));
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

    private record OllamaRequest(string Model, string Prompt, bool Stream);
}

public readonly record struct OllamaGenerationOutcome(string Text, int? PromptEvalCount, int? EvalCount);
