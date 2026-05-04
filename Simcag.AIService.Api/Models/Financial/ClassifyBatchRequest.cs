namespace Simcag.AIService.Api.Models.Financial;

public sealed record ClassifyBatchRequest(IReadOnlyList<ClassifyBatchItemRequest> Items);
