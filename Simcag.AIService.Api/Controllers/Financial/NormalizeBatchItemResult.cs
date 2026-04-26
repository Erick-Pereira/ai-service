using Simcag.AIService.Application.Contracts;

namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record NormalizeBatchItemResult(string? Id, bool Success, string? Error, NormalizedNameResult? Normalized);
