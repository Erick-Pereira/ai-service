namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record ClassifyBatchRequest(IReadOnlyList<ClassifyBatchItemRequest> Items);
