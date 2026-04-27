using FluentAssertions;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Services;
using Simcag.Shared.Events;
using Xunit;

namespace Simcag.AIService.Tests;

public sealed class FinancialEnrichmentDerivationTests
{
    [Fact]
    public void ComputeOverall_IgnoresWeakProductConfidence_WhenNoStructuredProduct()
    {
        var cat = new CategoryResult(Guid.NewGuid(), "Outro", 0.88m, "x", false);
        var sup = new SupplierExtractionResult(
            "ACME",
            "ACME",
            null,
            0.9m,
            false,
            new ProductExtractionResult(null, null, Array.Empty<string>(), 0.35m, true));

        FinancialEnrichmentConfidence.ComputeOverall(cat, sup).Should().Be(0.88m);
    }

    [Fact]
    public void ComputeOverall_IncludesProductConfidence_WhenStructuredProductPresent()
    {
        var cat = new CategoryResult(Guid.NewGuid(), "Hardware", 0.95m, "x", false);
        var sup = new SupplierExtractionResult(
            "ACME",
            "ACME",
            null,
            0.92m,
            false,
            new ProductExtractionResult("Intel", "i7", Array.Empty<string>(), 0.4m, true));

        FinancialEnrichmentConfidence.ComputeOverall(cat, sup).Should().Be(0.4m);
    }

    [Fact]
    public void Build_UsesExtractedItems_WhenProvided()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "d1",
            RawText = "ignored for items",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = string.Empty,
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = new List<object>
            {
                new FinancialItem { Description = "Taxa condomínio", Amount = 450m },
                new FinancialItem { Description = "Água", Amount = 22.5m }
            }
        };

        var sup = new SupplierExtractionResult("", "", null, 0.5m, true,
            new ProductExtractionResult("X", "Y", new[] { "z" }, 0.9m, false));

        var items = FinancialEnrichmentItemBuilder.Build(raw, sup);

        items.Should().HaveCount(2);
        items[0].Description.Should().Be("Taxa condomínio");
        items[0].Amount.Should().Be(450m);
        items[1].Amount.Should().Be(22.5m);
    }

    [Fact]
    public void Build_AppliesTotalFromExtractedFields_OnHeuristicDescriptionLine()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "d1",
            RawText = "Descrição genérica",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = string.Empty,
            ExtractedFields = new Dictionary<string, object?> { ["ValorTotal"] = 199.9m },
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

        var sup = new SupplierExtractionResult("", "", null, 0.5m, true,
            new ProductExtractionResult(null, null, Array.Empty<string>(), 0.35m, true));

        var items = FinancialEnrichmentItemBuilder.Build(raw, sup);

        items.Should().ContainSingle();
        items[0].Amount.Should().Be(199.9m);
    }

    [Fact]
    public void Build_ParsesBrlAmount_FromRawText_WhenNoExtractedTotal()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "d1",
            RawText = "Total a pagar R$ 1.234,56 fim",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = string.Empty,
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

        var sup = new SupplierExtractionResult("", "", null, 0.5m, true,
            new ProductExtractionResult(null, null, Array.Empty<string>(), 0.35m, true));

        var items = FinancialEnrichmentItemBuilder.Build(raw, sup);

        items.Should().ContainSingle();
        items[0].Amount.Should().Be(1234.56m);
    }
}
