namespace Simcag.AIService.Api.Models.Insights;

public sealed class NarrateOperationalInsightsRequest
{
    public string? Language { get; init; }

    public IReadOnlyList<NarrateOperationalInsightItemRequest>? Items { get; init; }
}

public sealed class NarrateOperationalInsightItemRequest
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Severity { get; init; }
    public int ImpactScore { get; init; }
    public string? SimpleExplanation { get; init; }
    public Dictionary<string, string>? Evidence { get; init; }
}
