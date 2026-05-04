namespace Simcag.AIService.Api.Models.Financial;

public sealed record NormalizeBatchRequest(IReadOnlyList<NormalizeBatchItemRequest> Items);
