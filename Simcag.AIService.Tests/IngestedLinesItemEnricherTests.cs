using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using Simcag.AIService.Application.Services;
using Xunit;

namespace Simcag.AIService.Tests;

public sealed class IngestedLinesItemEnricherTests
{
    [Fact]
    public void Enrich_merges_quantity_and_unit_from_ingestion_lines()
    {
        var ingestedJson =
            """[{"description":"Camera IP Full HD 2MP","amount":10680,"quantity":12,"unitPrice":890}]""";

        var raw = new RawFinancialDataEvent
        {
            DocumentId = Guid.NewGuid().ToString(),
            ExtractedFields = new Dictionary<string, object?> { ["ingestedLinesJson"] = ingestedJson },
        };

        var items = new List<FinancialItem>
        {
            new()
            {
                Description = "Camera IP Full HD 2MP",
                Amount = 10680m,
            },
        };

        var enriched = IngestedLinesItemEnricher.Enrich(items, raw);

        Assert.Single(enriched);
        Assert.Equal(12, enriched[0].Quantity);
        Assert.Equal(890m, enriched[0].UnitPrice);
    }

    [Fact]
    public void Enrich_repairs_when_llm_qty_one_and_unit_equals_line_total()
    {
        var ingestedJson =
            """[{"description":"Camera IP Full HD 2MP","amount":10680,"quantity":12,"unitPrice":890}]""";

        var raw = new RawFinancialDataEvent
        {
            DocumentId = Guid.NewGuid().ToString(),
            ExtractedFields = new Dictionary<string, object?> { ["ingestedLinesJson"] = ingestedJson },
        };

        var items = new List<FinancialItem>
        {
            new()
            {
                Description = "Camera IP Full HD 2MP",
                Amount = 10680m,
                Quantity = 1,
                UnitPrice = 10680m,
            },
        };

        var enriched = IngestedLinesItemEnricher.Enrich(items, raw);

        Assert.Equal(12, enriched[0].Quantity);
        Assert.Equal(890m, enriched[0].UnitPrice);
    }

    [Fact]
    public void Enrich_repairs_when_llm_unit_equals_line_total_with_quantity()
    {
        var ingestedJson =
            """[{"description":"Camera IP Full HD 2MP","amount":10680,"quantity":12,"unitPrice":890}]""";

        var raw = new RawFinancialDataEvent
        {
            DocumentId = Guid.NewGuid().ToString(),
            ExtractedFields = new Dictionary<string, object?> { ["ingestedLinesJson"] = ingestedJson },
        };

        var items = new List<FinancialItem>
        {
            new()
            {
                Description = "Camera IP Full HD 2MP",
                Amount = 10680m,
                Quantity = 12,
                UnitPrice = 10680m,
            },
        };

        var enriched = IngestedLinesItemEnricher.Enrich(items, raw);

        Assert.Equal(12, enriched[0].Quantity);
        Assert.Equal(890m, enriched[0].UnitPrice);
    }
}
