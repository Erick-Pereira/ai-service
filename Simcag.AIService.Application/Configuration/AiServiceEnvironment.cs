using System.Globalization;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Leitura centralizada de variáveis de ambiente do domínio financeiro (sem IConfiguration/appsettings).
/// </summary>
public static class AiServiceEnvironment
{
    public static string ModelName =>
        Environment.GetEnvironmentVariable("MODEL_NAME") is { } m && !string.IsNullOrWhiteSpace(m)
            ? m.Trim()
            : "llama3.1";

    public static TimeSpan InferenceCacheTtl =>
        TimeSpan.FromHours(int.Parse(Environment.GetEnvironmentVariable("AI_INFERENCE_CACHE_TTL_HOURS") ?? "72"));

    public static TimeSpan SupplierNormalizationCacheTtl =>
        TimeSpan.FromHours(int.Parse(Environment.GetEnvironmentVariable("SUPPLIER_NORMALIZATION_TTL_HOURS") ?? "168"));

    public static TimeSpan IdempotencyTtl =>
        TimeSpan.FromHours(int.Parse(Environment.GetEnvironmentVariable("IDEMPOTENCY_TTL_HOURS") ?? "24"));

    /// <summary>Limite técnico para observabilidade (OverallConfidence abaixo disso é logado como baixa confiança).</summary>
    public static decimal LowConfidenceThreshold =>
        decimal.TryParse(
            Environment.GetEnvironmentVariable("AI_LOW_CONFIDENCE_THRESHOLD"),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var v) && v is > 0m and <= 1m
            ? v
            : 0.55m;
}
