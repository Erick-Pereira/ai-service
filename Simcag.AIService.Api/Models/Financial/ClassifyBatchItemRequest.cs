namespace Simcag.AIService.Api.Models.Financial;

public sealed record ClassifyBatchItemRequest(string? Id, string RawText, string? DocumentType, string? Source);
