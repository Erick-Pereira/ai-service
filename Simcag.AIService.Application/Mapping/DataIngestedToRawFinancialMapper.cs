using System.Text.Json;
using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Mapping;

/// <summary>
/// Converte <see cref="DataIngestedEvent"/> canónico para <see cref="RawFinancialDataEvent"/> usado pelo pipeline de enriquecimento.
/// </summary>
public static class DataIngestedToRawFinancialMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RawFinancialDataEvent ToRawFinancial(DataIngestedEvent ingested)
    {
        var extra = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (ingested.ExtractedFields.Amount is { } amount)
            extra["amount"] = amount;
        if (ingested.ExtractedFields.Date is { } date)
            extra["date"] = date;
        if (!string.IsNullOrWhiteSpace(ingested.ExtractedFields.Description))
            extra["description"] = ingested.ExtractedFields.Description;
        if (!string.IsNullOrWhiteSpace(ingested.ExtractedFields.SupplierName))
            extra["supplierName"] = ingested.ExtractedFields.SupplierName;
        if (!string.IsNullOrWhiteSpace(ingested.ExtractedFields.SupplierTaxId))
            extra["supplierTaxId"] = ingested.ExtractedFields.SupplierTaxId;

        foreach (var kv in ingested.ExtractedFields.Extra)
            extra[kv.Key] = kv.Value;

        if (ingested.ExtractedFields.Lines is { Count: > 0 } lines)
            extra["ingestedLinesJson"] = JsonSerializer.Serialize(lines, JsonOpts);

        return new RawFinancialDataEvent
        {
            DocumentId = ingested.DocumentId.ToString(),
            TenantId = ingested.TenantId.ToString(),
            UploadedBy = ingested.UploadedBy,
            RawText = ingested.RawText,
            DocumentType = ingested.DocumentType,
            Source = ingested.Source,
            FileHash = ingested.FileHash,
            ExtractedFields = extra,
            OccurredAt = ingested.UploadedAt,
            Timestamp = ingested.UploadedAt
        };
    }
}
