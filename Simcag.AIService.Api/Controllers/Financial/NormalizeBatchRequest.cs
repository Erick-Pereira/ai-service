namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record NormalizeBatchRequest(IReadOnlyList<NormalizeBatchItemRequest> Items);
