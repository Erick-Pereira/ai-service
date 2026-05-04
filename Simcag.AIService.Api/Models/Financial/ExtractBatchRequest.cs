namespace Simcag.AIService.Api.Models.Financial;

public sealed record ExtractBatchRequest(IReadOnlyList<ExtractBatchItemRequest> Items);
