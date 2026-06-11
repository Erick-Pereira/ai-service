using System.Text.Json;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Preenche quantidade e preço unitário a partir das linhas estruturadas da ingestão
/// quando o LLM devolve apenas o total da linha.
/// </summary>
public static class IngestedLinesItemEnricher
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<FinancialItem> Enrich(
        IReadOnlyList<FinancialItem> items,
        RawFinancialDataEvent raw)
    {
        var ingested = TryParseIngestedLines(raw);
        if (ingested.Count == 0 || items.Count == 0)
            return items;

        return items.Select(item => MergeFromIngestion(item, ingested)).ToList();
    }

    private static FinancialItem MergeFromIngestion(FinancialItem item, IReadOnlyList<IngestedExpenseLine> ingested)
    {
        var match = FindBestMatch(item, ingested);
        if (match is null)
            return item;

        if (HasConsistentQuantityUnit(item, match))
            return item;

        var qty = match.Quantity is > 0m ? (int)Math.Round(match.Quantity.Value, MidpointRounding.AwayFromZero) : (int?)null;
        var unit = match.UnitPrice;
        if (unit is null or <= 0 && qty is > 0 && match.Amount > 0)
            unit = Math.Round(match.Amount / qty.Value, 4, MidpointRounding.AwayFromZero);

        if (qty is null or <= 0 && unit is null or <= 0)
            return item;

        var repaired = FinancialLineItemSemanticNormalizer.Repair(
            item.Description ?? string.Empty,
            item.Amount,
            qty ?? item.Quantity,
            unit ?? item.UnitPrice);

        return new FinancialItem
        {
            Description = string.IsNullOrWhiteSpace(repaired.CleanDescription)
                ? item.Description
                : repaired.CleanDescription,
            Amount = item.Amount,
            Quantity = repaired.Quantity ?? qty ?? item.Quantity,
            UnitPrice = repaired.UnitPrice ?? unit ?? item.UnitPrice,
            ItemCode = item.ItemCode,
        };
    }

    private static IngestedExpenseLine? FindBestMatch(FinancialItem item, IReadOnlyList<IngestedExpenseLine> ingested)
    {
        var desc = NormalizeKey(item.Description);
        if (string.IsNullOrEmpty(desc))
            return null;

        IngestedExpenseLine? best = null;
        var bestScore = 0;

        foreach (var line in ingested)
        {
            var key = NormalizeKey(line.Description);
            if (string.IsNullOrEmpty(key))
                continue;

            var score = ScoreMatch(desc, key);
            if (score > bestScore)
            {
                bestScore = score;
                best = line;
            }
        }

        return bestScore >= 60 ? best : null;
    }

    private static bool HasConsistentQuantityUnit(FinancialItem item, IngestedExpenseLine ingestedMatch)
    {
        if (item.Quantity is not > 0 || item.UnitPrice is not > 0)
            return false;

        if (Math.Abs(item.Quantity.Value * item.UnitPrice.Value - item.Amount) > 0.05m)
            return false;

        var unitNearLineTotal = Math.Abs(item.UnitPrice.Value - item.Amount) <= item.Amount * 0.02m;

        // qty=1 + unit≈lineTotal is the classic LLM mistake (total copied as unit price).
        if (item.Quantity == 1 && unitNearLineTotal)
            return false;

        // unit≈lineTotal while ingestion has multi-qty is also inconsistent.
        if (unitNearLineTotal && ingestedMatch.Quantity is > 1m)
            return false;

        return true;
    }

    private static int ScoreMatch(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase))
            return 80;

        var tokensA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokensB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokensA.Length == 0 || tokensB.Length == 0)
            return 0;

        var overlap = tokensA.Count(t => tokensB.Any(u => u.Equals(t, StringComparison.OrdinalIgnoreCase)));
        return (int)Math.Round(100.0 * overlap / Math.Max(tokensA.Length, tokensB.Length));
    }

    private static string NormalizeKey(string? text) =>
        FinancialLineItemSemanticNormalizer.ToSearchQueryLabel(text ?? string.Empty, 96).ToLowerInvariant();

    private static List<IngestedExpenseLine> TryParseIngestedLines(RawFinancialDataEvent raw)
    {
        if (!raw.ExtractedFields.TryGetValue("ingestedLinesJson", out var jsonObj))
            return new List<IngestedExpenseLine>();

        var json = jsonObj switch
        {
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString() ?? string.Empty,
            _ => JsonSerializer.Serialize(jsonObj, JsonOpts),
        };

        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestedExpenseLine>();

        try
        {
            return JsonSerializer.Deserialize<List<IngestedExpenseLine>>(json, JsonOpts) ?? new List<IngestedExpenseLine>();
        }
        catch (JsonException)
        {
            return new List<IngestedExpenseLine>();
        }
    }
}
