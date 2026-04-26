using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Serviço de normalização de nomes (fornecedores, descrições).
/// Remove acentos, padroniza case, remove palavras comuns.
/// </summary>
public sealed class NameNormalizationService : INameNormalizationService
{
    private const string CachePrefix = "ai-service:supplier-normalization:";
    private readonly INormalizationCache? _cache;
    private readonly TimeSpan _ttl;

    public NameNormalizationService(INormalizationCache? cache = null)
    {
        _cache = cache;
        _ttl = AiServiceEnvironment.SupplierNormalizationCacheTtl;
    }

    public Task<NormalizedNameResult> NormalizeAsync(string rawName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return Task.FromResult(new NormalizedNameResult(
                OriginalName: rawName,
                NormalizedName: rawName,
                Confidence: 1.0m,
                UsedFallback: false));
        }

        return NormalizeWithCacheAsync(rawName, ct);
    }

    private async Task<NormalizedNameResult> NormalizeWithCacheAsync(string rawName, CancellationToken ct)
    {
        var cache = _cache;
        if (cache is not null)
        {
            var key = CachePrefix + StableKey(rawName);
            var cached = await cache.GetAsync(key, ct);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                return new NormalizedNameResult(
                    OriginalName: rawName,
                    NormalizedName: cached,
                    Confidence: 0.9m,
                    UsedFallback: false);
            }

            var normalized = Normalize(rawName);
            await cache.SetAsync(key, normalized, _ttl, ct);
            return new NormalizedNameResult(
                OriginalName: rawName,
                NormalizedName: normalized,
                Confidence: 0.9m,
                UsedFallback: false);
        }

        var noCacheNormalized = Normalize(rawName);
        return new NormalizedNameResult(
            OriginalName: rawName,
            NormalizedName: noCacheNormalized,
            Confidence: 0.9m,
            UsedFallback: false);
    }

    private static string StableKey(string input)
    {
        // small stable key without extra deps: normalize whitespace + upper and use GetHashCode is not stable cross-process.
        // Use SHA256 from BCL.
        var canonical = System.Text.RegularExpressions.Regex.Replace(input.Trim().ToUpperInvariant(), @"\s+", " ");
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static string Normalize(string input)
    {
        var cleaned = input.Trim()
            .ToUpperInvariant();

        // Remove termos comuns
        var removals = new[] { " LTDA", " LTDA.", " ME", " EPP", " EIRELI", " COMERCIO", " COM.", " SERVICOS", " SVC", " IMPORTACAO", " IMP.", " EXPORTACAO", " EXP." };
        foreach (var r in removals)
            cleaned = cleaned.Replace(r, "");

        // Remove acentos
        cleaned = new string(cleaned.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

        // Espaços múltiplos
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }
}
