namespace Simcag.AIService.Application.Contracts;

public sealed record CategoryResult(Guid CategoryId, string CategoryName, decimal Confidence, string Reasoning, bool UsedFallback);
