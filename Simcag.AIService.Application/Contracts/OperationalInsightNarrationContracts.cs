namespace Simcag.AIService.Application.Contracts;

/// <summary>Pedido de narração explicativa sobre insights já calculados (não recalcula métricas).</summary>
public sealed class NarrateOperationalInsightsInput
{
    public string Language { get; init; } = "pt";

    public IReadOnlyList<NarrateOperationalInsightItemInput> Items { get; init; } =
        Array.Empty<NarrateOperationalInsightItemInput>();
}

public sealed class NarrateOperationalInsightItemInput
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Severity { get; init; } = "";
    public int ImpactScore { get; init; }
    public string SimpleExplanation { get; init; } = "";
    public IReadOnlyDictionary<string, string>? Evidence { get; init; }
}

public sealed class NarrateOperationalInsightsResult
{
    public string ExecutiveSummary { get; init; } = "";
    public IReadOnlyList<NarrateOperationalInsightItemNarrative> Items { get; init; } =
        Array.Empty<NarrateOperationalInsightItemNarrative>();
}

public sealed class NarrateOperationalInsightItemNarrative
{
    public string Id { get; init; } = "";
    public string SimpleExplanation { get; init; } = "";
    public string WhyItMatters { get; init; } = "";
    public string WhatToDo { get; init; } = "";
    public string DetailedExplanation { get; init; } = "";
}
