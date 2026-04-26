namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record ExtractBatchRequest(IReadOnlyList<ExtractBatchItemRequest> Items);
