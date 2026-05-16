using System.Globalization;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Resiliência da pipeline de inferência Ollama (env-first, alinhado a <see cref="AiServiceEnvironment"/>).
/// </summary>
public sealed class OllamaResilienceOptions
{
    /// <summary>Workers que drenam a fila (concorrência real contra o Ollama).</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Capacidade da fila interna (backpressure quando cheia).</summary>
    public int QueueCapacity { get; set; } = 64;

    /// <summary>Timeout por tentativa HTTP (linked cancellation), segundos.</summary>
    public int PerAttemptTimeoutSeconds { get; set; } = 90;

    /// <summary>Timeout global do <see cref="HttpClient"/> (deve ser ≥ soma de tentativas).</summary>
    public int HttpClientTimeoutSeconds { get; set; } = 600;

    /// <summary>Retentativas após a primeira tentativa (total = 1 + MaxRetries).</summary>
    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public int CircuitFailureThreshold { get; set; } = 5;

    public int CircuitOpenSeconds { get; set; } = 60;

    /// <summary>Modelo operacional de fallback após esgotar retentativas no modelo pedido (ex.: <c>llama3.2:3b</c>).</summary>
    public string? OperationalFallbackModel { get; set; }

    public static OllamaResilienceOptions FromEnvironment()
    {
        static int EnvInt(string key, int defaultValue, int min = 1, int? max = null)
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return defaultValue;
            v = Math.Max(min, v);
            return max is { } m ? Math.Min(v, m) : v;
        }

        var perAttempt = EnvInt("OLLAMA_INFERENCE_PER_ATTEMPT_TIMEOUT_SECONDS", 90, min: 15, max: 600);
        var httpTotal = EnvInt("OLLAMA_TIMEOUT_SECONDS", 600, min: 60, max: 3600);
        if (httpTotal < perAttempt * 3)
            httpTotal = Math.Min(3600, perAttempt * 3 + 30);

        return new OllamaResilienceOptions
        {
            MaxConcurrency = EnvInt("OLLAMA_INFERENCE_MAX_CONCURRENCY", 2, min: 1, max: 32),
            QueueCapacity = EnvInt("OLLAMA_INFERENCE_QUEUE_CAPACITY", 64, min: 8, max: 4096),
            PerAttemptTimeoutSeconds = perAttempt,
            HttpClientTimeoutSeconds = httpTotal,
            MaxRetries = EnvInt("OLLAMA_INFERENCE_MAX_RETRIES", 2, min: 0, max: 8),
            RetryBaseDelayMilliseconds = EnvInt("OLLAMA_INFERENCE_RETRY_BASE_MS", 500, min: 50, max: 30_000),
            CircuitFailureThreshold = EnvInt("OLLAMA_INFERENCE_CIRCUIT_FAILURE_THRESHOLD", 5, min: 1, max: 100),
            CircuitOpenSeconds = EnvInt("OLLAMA_INFERENCE_CIRCUIT_OPEN_SECONDS", 60, min: 5, max: 3600),
            OperationalFallbackModel = NormalizeModelEnv("OLLAMA_FALLBACK_MODEL")
        };
    }

    private static string? NormalizeModelEnv(string key)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
