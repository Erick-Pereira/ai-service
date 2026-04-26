using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Extração de fornecedor (nome, documento fiscal) e de produto/serviço (marca, modelo, funcionalidades), com confiança e fallback por bloco.
/// </summary>
public sealed class SupplierExtractionService : ISupplierExtractionService
{
    private const decimal DefaultAiSupplierConfidence = 0.85m;
    private const decimal DefaultAiProductConfidenceWhenPresent = 0.82m;

    private readonly IOllamaClient _ollama;
    private readonly ILogger<SupplierExtractionService> _logger;
    private readonly INameNormalizationService _nameNormalization;
    private readonly string _modelName;
    private readonly IAiInferenceCache _inferenceCache;
    private readonly TimeSpan _inferenceTtl;

    public SupplierExtractionService(
        IOllamaClient ollama,
        ILogger<SupplierExtractionService> logger,
        INameNormalizationService nameNormalization,
        IAiInferenceCache inferenceCache)
    {
        _ollama = ollama;
        _logger = logger;
        _nameNormalization = nameNormalization;
        _inferenceCache = inferenceCache;

        _modelName = AiServiceEnvironment.ModelName;
        _inferenceTtl = AiServiceEnvironment.InferenceCacheTtl;
    }

    public async Task<SupplierExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct)
    {
        try
        {
            if (await _ollama.IsAvailableAsync(ct))
            {
                try
                {
                    var prompt = BuildExtractionPrompt(financialData.RawText);
                    var rawResponse = await GenerateWithCacheAsync(prompt, ct);

                    if (!string.IsNullOrWhiteSpace(rawResponse))
                    {
                        var parsed = TryParseExtractionResponse(rawResponse);
                        if (parsed != null)
                        {
                            var normalized = parsed.NormalizedSupplierName;
                            if (string.IsNullOrWhiteSpace(normalized) && !string.IsNullOrWhiteSpace(parsed.RawSupplierName))
                            {
                                try
                                {
                                    var result = await _nameNormalization.NormalizeAsync(parsed.RawSupplierName, ct);
                                    normalized = string.IsNullOrWhiteSpace(result.NormalizedName)
                                        ? parsed.RawSupplierName.Trim()
                                        : result.NormalizedName;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Supplier name normalization failed; using raw name");
                                    normalized = parsed.RawSupplierName.Trim();
                                }
                            }

                            return new SupplierExtractionResult(
                                RawSupplierName: parsed.RawSupplierName,
                                NormalizedSupplierName: normalized ?? string.Empty,
                                TaxId: parsed.TaxId,
                                Confidence: parsed.SupplierConfidence,
                                UsedFallback: parsed.SupplierUsedFallback,
                                Product: parsed.Product);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "LLM supplier/product extraction failed for document {DocumentId}; using heuristic fallback",
                        financialData.DocumentId);
                }
            }

            return await ExtractFallbackAsync(financialData.RawText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heuristic fallback failed for document {DocumentId}", financialData.DocumentId);
            return new SupplierExtractionResult(
                RawSupplierName: string.Empty,
                NormalizedSupplierName: string.Empty,
                TaxId: null,
                Confidence: 0.1m,
                UsedFallback: true,
                Product: new ProductExtractionResult(null, null, Array.Empty<string>(), 0.1m, true));
        }
    }

    private static string BuildExtractionPrompt(string rawText) =>
        "You extract structured data from financial or procurement document text. Return a single JSON object with:\n" +
        "- supplierName: string (vendor/legal name if any, else empty string)\n" +
        "- taxId: string or null (CNPJ/CPF if any)\n" +
        "- brand: string or null (product/service brand)\n" +
        "- model: string or null (model name or SKU-like identifier)\n" +
        "- features: array of short strings (distinct features, specs, or service bullets; empty array if none)\n" +
        "- supplierConfidence: number 0-1 (your confidence in supplierName/taxId)\n" +
        "- supplierUsedFallback: boolean (true only if you are guessing supplier with weak evidence)\n" +
        "- productConfidence: number 0-1 (your confidence in brand/model/features)\n" +
        "- productUsedFallback: boolean (true if brand/model/features are uncertain or absent but you inferred weakly)\n" +
        "Be conservative: use low confidence and usedFallback true when evidence is weak.\n" +
        $"Document text:\n{rawText}";

    private async Task<string> GenerateWithCacheAsync(string prompt, CancellationToken ct)
    {
        var key = LlmInferenceCacheKeys.ForPrompt("supplier-extract", _modelName, prompt);
        var cached = await _inferenceCache.GetAsync(key, ct);
        if (!string.IsNullOrWhiteSpace(cached))
            return cached;

        var response = await _ollama.GenerateCompletionAsync(prompt, _modelName, ct);
        if (!string.IsNullOrWhiteSpace(response))
            await _inferenceCache.SetAsync(key, response, _inferenceTtl, ct);

        return response;
    }

    private sealed record ParsedExtraction(
        string RawSupplierName,
        string NormalizedSupplierName,
        string? TaxId,
        decimal SupplierConfidence,
        bool SupplierUsedFallback,
        ProductExtractionResult Product);

    private static ParsedExtraction? TryParseExtractionResponse(string response)
    {
        try
        {
            var json = StripMarkdownCodeFence(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("supplierName", out var nameProp))
                return null;

            var rawName = nameProp.GetString() ?? string.Empty;
            string? taxId = null;
            if (root.TryGetProperty("taxId", out var taxProp) && taxProp.ValueKind != JsonValueKind.Null)
                taxId = taxProp.GetString();

            var supplierConf = ReadDecimal(root, "supplierConfidence") ?? ReadDecimal(root, "confidence") ?? DefaultAiSupplierConfidence;
            supplierConf = Clamp01(supplierConf);
            var supplierFb = ReadBool(root, "supplierUsedFallback") ?? false;

            var brand = ReadOptionalString(root, "brand");
            var model = ReadOptionalString(root, "model");
            var features = ReadStringArray(root, "features");
            var hasProductSignal = !string.IsNullOrWhiteSpace(brand) || !string.IsNullOrWhiteSpace(model) || features.Count > 0;

            var productConf = ReadDecimal(root, "productConfidence") ?? ReadDecimal(root, "confidence");
            var productFb = ReadBool(root, "productUsedFallback");

            decimal pConf;
            bool pFb;
            if (productConf.HasValue)
            {
                pConf = Clamp01(productConf.Value);
                pFb = productFb ?? false;
            }
            else if (hasProductSignal)
            {
                pConf = DefaultAiProductConfidenceWhenPresent;
                pFb = productFb ?? false;
            }
            else
            {
                pConf = 0.35m;
                pFb = productFb ?? true;
            }

            var product = new ProductExtractionResult(brand, model, features, pConf, pFb);

            return new ParsedExtraction(
                RawSupplierName: rawName,
                NormalizedSupplierName: string.Empty,
                TaxId: taxId,
                SupplierConfidence: supplierConf,
                SupplierUsedFallback: supplierFb,
                Product: product);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripMarkdownCodeFence(string response)
    {
        var t = response.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = t.IndexOf('\n');
            if (firstNl > 0)
                t = t[(firstNl + 1)..];
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0)
                t = t[..end];
        }

        return t.Trim();
    }

    private static string? ReadOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => decimal.TryParse(el.GetString(), out var x) ? x : null,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }
        }

        return list;
    }

    private static decimal Clamp01(decimal v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private async Task<SupplierExtractionResult> ExtractFallbackAsync(string rawText, CancellationToken ct)
    {
        var lines = rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = lines.Where(l => l.Length > 5 && l.Count(char.IsUpper) > 2).OrderByDescending(l => l.Length).FirstOrDefault() ?? string.Empty;

        string normalized;
        try
        {
            var result = await _nameNormalization.NormalizeAsync(candidates, ct);
            normalized = string.IsNullOrWhiteSpace(result.NormalizedName) ? candidates.Trim() : result.NormalizedName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supplier name normalization failed in fallback; using raw candidate");
            normalized = candidates.Trim();
        }

        var taxId = ExtractTaxId(rawText);

        // Heurística: linha escolhida costuma ser descrição de produto; expõe como feature para o evento enriquecido.
        IReadOnlyList<string> productFeatures = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(candidates))
            productFeatures = new[] { candidates.Trim() }.ToList();

        var product = new ProductExtractionResult(null, null, productFeatures, 0.45m, true);

        return new SupplierExtractionResult(
            RawSupplierName: candidates,
            NormalizedSupplierName: normalized,
            TaxId: taxId,
            Confidence: 0.5m,
            UsedFallback: true,
            Product: product);
    }

    private static string? ExtractTaxId(string text)
    {
        var patterns = new[] { @"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}", @"\d{14}", @"\d{3}\.\d{3}\.\d{3}-\d{2}", @"\d{11}" };
        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
            if (match.Success)
                return match.Value;
        }

        return null;
    }
}
