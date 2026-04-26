namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record ClassifyBatchItemRequest(string? Id, string RawText, string? DocumentType, string? Source);
