namespace Simcag.AIService.Application.Contracts;

public sealed record NormalizedNameResult(string OriginalName, string NormalizedName, decimal Confidence, bool UsedFallback);
