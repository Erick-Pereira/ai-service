namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record ExtractBatchItemRequest(string? Id, string RawText, string? DocumentType, string? Source);
