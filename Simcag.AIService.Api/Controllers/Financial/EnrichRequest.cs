namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record EnrichRequest(
    string RawText,
    string? DocumentId,
    string? DocumentType,
    string? Source,
    string? FileHash,
    Dictionary<string, object?>? ExtractedFields,
    DateTime OccurredAt);
