using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Utilities;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using Simcag.Shared.Telemetry;
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

    private const int MaxSupplierPromptChars = 12_000;

    public async Task<SupplierExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct, string? supplierPromptTextOverride = null)
    {
        try
        {
            if (await _ollama.IsAvailableAsync(ct))
            {
                try
                {
                    var textForPrompt = string.IsNullOrWhiteSpace(supplierPromptTextOverride)
                        ? financialData.RawText
                        : supplierPromptTextOverride;
                    textForPrompt = TruncateForPrompt(textForPrompt ?? string.Empty, MaxSupplierPromptChars);
                    var prompt = BuildExtractionPrompt(textForPrompt);
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
                                    // Se normalização remove todo o nome ou termos essenciais (LTDA, ME, LTDA.), use o nome raw
                                    if (string.IsNullOrWhiteSpace(result.NormalizedName) || 
                                        !result.NormalizedName.Contains(parsed.RawSupplierName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogWarning("Normalização eliminou informações críticas do fornecedor; usando nome original: {RawName}", parsed.RawSupplierName);
                                        normalized = parsed.RawSupplierName.Trim();
                                    }
                                    else
                                    {
                                        normalized = result.NormalizedName;
                                    }
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

    private static string TruncateForPrompt(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars];

    private static string BuildExtractionPrompt(string rawText) =>
        "You extract structured data from Brazilian financial document text (NF-e or NFS-e). " +
        "Return a single JSON object with:\n" +
        "- supplierName: string (PRESTADOR DE SERVIÇOS or TOMADOR if applicable, else empty string)\n" +
        "- taxId: string or null (CNPJ/CPF do prestador ou tomador - buscar em campos como 'CNPJ', 'CPF', '18.456.782/0001-22')\n" +
        "- brand: string or null (marca do produto/serviço)\n" +
        "- model: string or null (modelo ou SKU)\n" +
        "- features: array of short strings (especificações, bullets de serviço; empty if none)\n" +
        "- supplierConfidence: number 0-1 (confiança na extração do nome e documento fiscal)\n" +
        "- supplierUsedFallback: boolean (true apenas se estiver adivinhando com evidência fraca)\n" +
        "- productConfidence: number 0-1 (confiança em brand/model/features)\n" +
        "- productUsedFallback: boolean (true se brand/model/features estão incertos ou ausentes)\n" +
        "Be conservative: use low confidence and usedFallback=true quando evidência for fraca.\n" +
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
        if (!LlmStructuredJsonParser.TryParseJsonObject(response, "supplier_extract", out var doc))
            return null;

        using (doc)
        {
            try
            {
                var root = doc.RootElement;

                if (!root.TryGetProperty("supplierName", out var nameProp))
                {
                    SimcagMeters.AiLlmParseFailures.Add(1,
                        new KeyValuePair<string, object?>("kind", "supplier_extract"),
                        new KeyValuePair<string, object?>("reason", "missing_supplierName"));
                    return null;
                }

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
        var hint = BrazilianDocumentSupplierExtractor.TryExtract(rawText);
        if (!string.IsNullOrWhiteSpace(hint.Name) || !string.IsNullOrWhiteSpace(hint.TaxId))
        {
            var normalizedName = hint.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                try
                {
                    var result = await _nameNormalization.NormalizeAsync(normalizedName, ct);
                    if (!string.IsNullOrWhiteSpace(result.NormalizedName))
                        normalizedName = result.NormalizedName;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Supplier name normalization failed in structured fallback");
                }
            }

            return new SupplierExtractionResult(
                RawSupplierName: hint.Name ?? string.Empty,
                NormalizedSupplierName: normalizedName,
                TaxId: hint.TaxId,
                Confidence: 0.72m,
                UsedFallback: true,
                Product: new ProductExtractionResult(null, null, Array.Empty<string>(), 0.35m, true));
        }

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
