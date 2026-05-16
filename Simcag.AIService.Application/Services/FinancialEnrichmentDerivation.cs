using Simcag.AIService.Application.Contracts;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Cálculo de confiança agregada e montagem de linhas de item para o evento enriquecido (regras técnicas, sem decisão de negócio).
/// </summary>
public static class FinancialEnrichmentConfidence
{
    /// <summary>
    /// Quando não há sinal estruturado de produto/serviço, a confiança do bloco "produto" não deve penalizar o resultado geral
    /// (ex.: NF de serviço com categoria e fornecedor fortes).
    /// </summary>
    public static decimal ComputeOverall(CategoryResult category, SupplierExtractionResult supplier)
    {
        var product = supplier.Product;
        var hasStructuredProduct =
            !string.IsNullOrWhiteSpace(product.Brand)
            || !string.IsNullOrWhiteSpace(product.Model)
            || product.Features.Count > 0;

        var core = Math.Min(category.Confidence, supplier.Confidence);
        return hasStructuredProduct ? Math.Min(core, product.Confidence) : core;
    }
}

/// <summary>
/// Deriva itens e valores monetários a partir do evento bruto + resultado de extração.
/// </summary>
public static class FinancialEnrichmentItemBuilder
{
    private static readonly Regex BrlMoney = new(
        @"R\$\s*(\d{1,3}(?:\.\d{3})*(?:,\d{2})?|\d+,\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<FinancialItem> Build(RawFinancialDataEvent raw, SupplierExtractionResult supplierResult)
    {
        if (raw.ExtractedItems is { } extracted && extracted.Count > 0)
        {
            var fromUpstream = new List<FinancialItem>(extracted.Count);
            foreach (var it in extracted)
            {
                if (!TryMapExtractedLine(it, out var row))
                    continue;
                if (string.IsNullOrWhiteSpace(row.Description) && row.Amount <= 0)
                    continue;
                fromUpstream.Add(row);
            }

            if (fromUpstream.Count > 0)
                return fromUpstream;
        }

        var items = new List<FinancialItem>();
        var p = supplierResult.Product;

        if (!string.IsNullOrWhiteSpace(p.Brand) || !string.IsNullOrWhiteSpace(p.Model))
        {
            var line = string.Join(' ', new[] { p.Brand, p.Model }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (line.Length > 0)
                items.Add(new FinancialItem { Description = line, Amount = 0m });
        }

        foreach (var f in p.Features)
        {
            if (!string.IsNullOrWhiteSpace(f))
                items.Add(new FinancialItem { Description = f.Trim(), Amount = 0m });
        }

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(raw.RawText))
        {
            var compact = raw.RawText.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (compact.Contains("  ", StringComparison.Ordinal))
                compact = compact.Replace("  ", " ", StringComparison.Ordinal);
            if (compact.Length > 512)
                compact = compact[..512];
            if (compact.Length > 0)
                items.Add(new FinancialItem { Description = compact, Amount = 0m });
        }

        var resolved = TryResolvePrimaryAmount(raw);
        if (resolved.HasValue && items.Count > 0)
        {
            var last = items[^1];
            items[^1] = new FinancialItem { Description = last.Description, Amount = resolved.Value };
        }

        return items;
    }

    private static decimal? TryResolvePrimaryAmount(RawFinancialDataEvent raw)
    {
        foreach (var requested in AmountFieldKeys)
        {
            foreach (var kv in raw.ExtractedFields)
            {
                if (!string.Equals(kv.Key, requested, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TryCoerceDecimal(kv.Value, out var d) && d > 0)
                    return d;
            }
        }

        var m = BrlMoney.Match(raw.RawText ?? string.Empty);
        if (!m.Success || m.Groups.Count < 2)
            return null;

        var brl = m.Groups[1].Value.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(brl, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null;
    }

    private static bool TryMapExtractedLine(object? it, out FinancialItem row)
    {
        row = new FinancialItem();
        switch (it)
        {
            case FinancialItem fi:
                row = FinancialLineItemSemanticNormalizer.NormalizeFinancialItem(fi);
                return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                var desc = string.Empty;
                if (je.TryGetProperty("description", out var d1) && d1.ValueKind == JsonValueKind.String)
                    desc = d1.GetString()?.Trim() ?? string.Empty;
                else if (je.TryGetProperty("Description", out var d2) && d2.ValueKind == JsonValueKind.String)
                    desc = d2.GetString()?.Trim() ?? string.Empty;

                var amt = 0m;
                if (je.TryGetProperty("amount", out var a1))
                    TryReadJsonMoney(a1, out amt);
                else if (je.TryGetProperty("Amount", out var a2))
                    TryReadJsonMoney(a2, out amt);

                int? qty = null;
                if (je.TryGetProperty("quantity", out var q1) && q1.ValueKind == JsonValueKind.Number && q1.TryGetInt32(out var qi) && qi > 0)
                    qty = qi;
                else if (je.TryGetProperty("Quantity", out var q2) && q2.ValueKind == JsonValueKind.Number && q2.TryGetInt32(out var q2i) && q2i > 0)
                    qty = q2i;

                decimal? unit = null;
                if (je.TryGetProperty("unitPrice", out var u1) && TryReadJsonMoney(u1, out var uDec) && uDec > 0m)
                    unit = uDec;
                else if (je.TryGetProperty("UnitPrice", out var u2) && TryReadJsonMoney(u2, out var uDec2) && uDec2 > 0m)
                    unit = uDec2;

                row = FinancialLineItemSemanticNormalizer.NormalizeFinancialItem(new FinancialItem
                {
                    Description = desc,
                    Amount = amt,
                    Quantity = qty,
                    UnitPrice = unit
                });
                return true;
            default:
                return false;
        }
    }

    private static readonly string[] AmountFieldKeys =
    [
        "TotalAmount", "totalAmount", "ValorTotal", "valor_total", "valorTotal",
        "Amount", "amount", "Total", "total", "Valor", "valor", "value"
    ];

    private static bool TryCoerceDecimal(object? value, out decimal amount)
    {
        amount = 0m;
        if (value is null)
            return false;

        switch (value)
        {
            case decimal d:
                amount = d;
                return amount > 0;
            case double f:
                amount = (decimal)f;
                return amount > 0;
            case float g:
                amount = (decimal)g;
                return amount > 0;
            case int i:
                amount = i;
                return amount > 0;
            case long l:
                amount = l;
                return amount > 0;
            case JsonElement el:
                return TryCoerceJsonElement(el, out amount);
            case string s:
                return TryParseDecimalString(s, out amount);
            default:
                return TryParseDecimalString(value.ToString(), out amount);
        }
    }

    private static bool TryCoerceJsonElement(JsonElement el, out decimal amount)
    {
        amount = 0m;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out amount) && amount > 0,
            JsonValueKind.String => TryParseDecimalString(el.GetString(), out amount),
            _ => false
        };
    }

    private static bool TryReadJsonMoney(JsonElement el, out decimal amount)
    {
        amount = 0m;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out amount),
            JsonValueKind.String => TryParseDecimalStringLenient(el.GetString(), out amount),
            _ => false
        };
    }

    private static bool TryParseDecimalStringLenient(string? s, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(s))
            return true;

        var t = s.Trim();
        if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
            return true;
        return decimal.TryParse(t, NumberStyles.Any, new CultureInfo("pt-BR"), out amount);
    }

    private static bool TryParseDecimalString(string? s, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var t = s.Trim();
        if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out amount) && amount > 0)
            return true;
        if (decimal.TryParse(t, NumberStyles.Any, new CultureInfo("pt-BR"), out amount) && amount > 0)
            return true;

        return false;
    }
}
