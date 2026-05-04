namespace Simcag.AIService.Api.Models.Financial;

public sealed record ExtractBatchItemRequest(string? Id, string RawText, string? DocumentType, string? Source);
