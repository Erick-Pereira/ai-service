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
}
