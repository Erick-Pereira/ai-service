using Simcag.Shared.Events;

namespace Simcag.AIService.Api.Controllers.Financial;

/// <summary>
/// Constrói <see cref="RawFinancialDataEvent"/> a partir de pedidos HTTP simples ou normaliza payloads já estruturados.
/// </summary>
public static class FinancialRawEventFactory
{
    public const int MaxBatchItems = 25;

    public static RawFinancialDataEvent FromClassifyOrExtract(string rawText, string? documentType, string? source) =>
        new()
        {
            DocumentId = Guid.NewGuid().ToString(),
            RawText = rawText,
            DocumentType = string.IsNullOrWhiteSpace(documentType) ? "Unknown" : documentType,
            Source = string.IsNullOrWhiteSpace(source) ? "api" : source,
            FileHash = string.Empty,
            ExtractedFields = new(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

    public static RawFinancialDataEvent FromEnrichRequest(EnrichRequest request) =>
        new()
        {
            DocumentId = string.IsNullOrWhiteSpace(request.DocumentId) ? Guid.NewGuid().ToString() : request.DocumentId,
            RawText = request.RawText,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "Unknown" : request.DocumentType,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "api" : request.Source,
            FileHash = request.FileHash ?? string.Empty,
            ExtractedFields = request.ExtractedFields ?? new(),
            OccurredAt = request.OccurredAt == default ? DateTime.UtcNow : request.OccurredAt,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

    /// <summary>Garante identificadores e timestamps mínimos para ingestão via API quando o cliente envia um evento parcial.</summary>
    public static RawFinancialDataEvent NormalizeFromApiPayload(RawFinancialDataEvent input)
    {
        var docId = string.IsNullOrWhiteSpace(input.DocumentId) ? Guid.NewGuid().ToString() : input.DocumentId.Trim();
        var occurred = input.OccurredAt == default ? DateTime.UtcNow : input.OccurredAt;
        var ts = input.Timestamp == default ? DateTime.UtcNow : input.Timestamp;
        var created = input.CreatedAt == default ? DateTime.UtcNow : input.CreatedAt;
        var eventId = input.EventId == Guid.Empty ? Guid.NewGuid() : input.EventId;

        return new RawFinancialDataEvent
        {
            EventId = eventId,
            CreatedAt = created,
            DocumentId = docId,
            RawText = input.RawText ?? string.Empty,
            DocumentType = string.IsNullOrWhiteSpace(input.DocumentType) ? "Unknown" : input.DocumentType,
            Source = string.IsNullOrWhiteSpace(input.Source) ? "api" : input.Source,
            FileHash = input.FileHash ?? string.Empty,
            ExtractedFields = input.ExtractedFields ?? new(),
            OccurredAt = occurred,
            Timestamp = ts,
            ExtractedItems = input.ExtractedItems
        };
    }
}
