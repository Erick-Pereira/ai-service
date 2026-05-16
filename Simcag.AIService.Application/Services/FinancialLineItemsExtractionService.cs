using System.Globalization;
using System.Text;
using System.Text.Json;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Utilities;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using Simcag.Shared.Telemetry;
using Microsoft.Extensions.Logging;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Usa Ollama para produzir JSON com linhas de despesa (PT-BR: valores 1.234,56).
/// Fallback: lista vazia + <see cref="FinancialLineItemsExtractionResult.UsedFallback"/> true.
/// </summary>
public sealed class FinancialLineItemsExtractionService : IFinancialLineItemsExtractionService
{
    private readonly IOllamaClient _ollama;
    private readonly ILogger<FinancialLineItemsExtractionService> _logger;
    private readonly IAiInferenceCache _inferenceCache;
    private readonly string _modelName;
    private readonly TimeSpan _inferenceTtl;
    private readonly int _maxDocumentChars;

    public FinancialLineItemsExtractionService(
        IOllamaClient ollama,
        ILogger<FinancialLineItemsExtractionService> logger,
        IAiInferenceCache inferenceCache)
    {
        _ollama = ollama;
        _logger = logger;
        _inferenceCache = inferenceCache;
        _modelName = AiServiceEnvironment.ModelName;
        _inferenceTtl = AiServiceEnvironment.InferenceCacheTtl;
        _maxDocumentChars = int.TryParse(Environment.GetEnvironmentVariable("AI_LINE_ITEMS_MAX_CHARS"), out var n) && n >= 2000
            ? Math.Min(n, 100_000)
            : 14_000;
    }

    public async Task<FinancialLineItemsExtractionResult> ExtractAsync(RawFinancialDataEvent financialData, CancellationToken ct)
    {
        var raw = financialData.RawText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);

        if (!await _ollama.IsAvailableAsync(ct))
            return new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);

        var snippet = raw.Length <= _maxDocumentChars ? raw : raw[.._maxDocumentChars];
        var prompt = BuildPrompt(snippet);

        try
        {
            var key = LlmInferenceCacheKeys.ForPrompt("line-items-extract", _modelName, prompt);
            var cached = await _inferenceCache.GetAsync(key, ct);
            string response;
            if (!string.IsNullOrWhiteSpace(cached))
                response = cached;
            else
            {
                response = await _ollama.GenerateCompletionAsync(prompt, _modelName, ct);
                if (!string.IsNullOrWhiteSpace(response))
                    await _inferenceCache.SetAsync(key, response, _inferenceTtl, ct);
            }

            if (string.IsNullOrWhiteSpace(response))
                return new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);

            var parsed = TryParseResponse(response);
            if (parsed is null)
            {
                _logger.LogWarning("LLM line-items response not parseable for document {DocumentId}", financialData.DocumentId);
                return new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);
            }

            _logger.LogInformation(
                "LLM extraiu {Count} linha(s) para documento {DocumentId} (confidence={Confidence:F2}, fallback={Fb})",
                parsed.Items.Count,
                financialData.DocumentId,
                parsed.Confidence,
                parsed.UsedFallback);

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM line-items extraction failed for document {DocumentId}", financialData.DocumentId);
            return new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);
        }
    }

    private static string BuildPrompt(string documentText)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "You extract STRUCTURED expense LINE ITEMS from Brazilian condominium / financial documents (Portuguese).");
        sb.AppendLine("Return ONE JSON object ONLY (no markdown), with this shape:");
        sb.AppendLine(@"  {""items"":[{""description"":""string"",""quantity"":1,""unitPrice"":820.00,""amount"":820.00}],""documentTitle"":""string or null"",""confidence"":0.85,""usedFallback"":false}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- amount (alias lineTotal): line total in BRL (JSON number with dot decimal). Convert Brazilian format 2.500,00 → 2500.00 , 450,00 → 450.00.");
        sb.AppendLine("- quantity: integer units for that row; default 1 when a single charge.");
        sb.AppendLine("- unitPrice: BRL per unit when the document shows unit price; omit or null if unknown.");
        sb.AppendLine("- One object per expense ROW (detail lines). Prefer detail lines over a single grand total when both exist.");
        sb.AppendLine("- description: PT-BR label ONLY (no R$, no amounts, no quantity, no column headers). If OCR glued values into the text, still output the clean label you infer.");
        sb.AppendLine("- If there are NO monetary line items, return items:[], confidence low, usedFallback:true.");
        sb.AppendLine("- confidence: 0-1 for how sure you are about items[]. usedFallback:true if you guessed.");
        sb.AppendLine();
        sb.AppendLine("Document text:");
        sb.Append(documentText);
        return sb.ToString();
    }

    private static FinancialLineItemsExtractionResult? TryParseResponse(string response)
    {
        if (!LlmStructuredJsonParser.TryParseJsonObject(response, "line_items_extract", out var doc))
            return null;

        using (doc)
        {
            try
            {
                var root = doc.RootElement;

                if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                {
                    SimcagMeters.AiLlmParseFailures.Add(1,
                        new KeyValuePair<string, object?>("kind", "line_items_extract"),
                        new KeyValuePair<string, object?>("reason", "missing_items_array"));
                    return null;
                }

                var list = new List<FinancialItem>();
                foreach (var row in itemsEl.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                        continue;

                    var desc = ReadString(row, "description") ?? ReadString(row, "descricao") ?? ReadString(row, "Descricao");
                    var amt = ReadAmount(row, "amount")
                        ?? ReadAmount(row, "lineTotal")
                        ?? ReadAmount(row, "valorTotal")
                        ?? ReadAmount(row, "valor")
                        ?? ReadAmount(row, "Valor");
                    var qty = ReadPositiveInt(row, "quantity", "qty", "quantidade", "Quantidade");
                    var unit = ReadAmount(row, "unitPrice")
                        ?? ReadAmount(row, "unit_price")
                        ?? ReadAmount(row, "valorUnitario")
                        ?? ReadAmount(row, "ValorUnitario")
                        ?? ReadAmount(row, "precoUnitario");

                    desc = desc?.Trim() ?? string.Empty;
                    if (amt is null or <= 0)
                        continue;

                    var repaired = FinancialLineItemSemanticNormalizer.Repair(desc, amt.Value, qty, unit);
                    if (string.IsNullOrWhiteSpace(repaired.CleanDescription))
                        continue;

                    list.Add(new FinancialItem
                    {
                        Description = repaired.CleanDescription,
                        Amount = repaired.LineTotal,
                        Quantity = repaired.Quantity,
                        UnitPrice = repaired.UnitPrice
                    });
                }

                var title = ReadString(root, "documentTitle") ?? ReadString(root, "titulo");
                var confidence = ReadConfidence(root);
                var usedFb = ReadBool(root, "usedFallback") ?? false;

                return new FinancialLineItemsExtractionResult(list, title, confidence, usedFb);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? ReadPositiveInt(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var el))
                continue;
            switch (el.ValueKind)
            {
                case JsonValueKind.Number when el.TryGetInt32(out var i) && i is > 0 and < 1_000_000:
                    return i;
                case JsonValueKind.Number when el.TryGetDecimal(out var d) && d > 0m && d == Math.Truncate(d):
                {
                    var n = (int)d;
                    if (n is > 0 and < 1_000_000)
                        return n;
                    break;
                }
                case JsonValueKind.String:
                {
                    var s = el.GetString();
                    if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var k) && k is > 0 and < 1_000_000)
                        return k;
                    break;
                }
            }
        }

        return null;
    }

    private static decimal? ReadAmount(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => ParseMoneyString(el.GetString()),
            _ => null
        };
    }

    private static decimal? ParseMoneyString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = s.Trim();
        if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv) && inv > 0)
            return inv;
        if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out var br) && br > 0)
            return br;
        var hasComma = t.Contains(',');
        var hasDot = t.Contains('.');
        if (hasComma && hasDot)
        {
            var norm = t.Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
            return decimal.TryParse(norm, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) && x > 0 ? x : null;
        }

        return null;
    }

    private static decimal ReadConfidence(JsonElement root)
    {
        if (!root.TryGetProperty("confidence", out var el))
            return 0.75m;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
            return Math.Clamp(d, 0m, 1m);
        return 0.75m;
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
}
