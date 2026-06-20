using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Services;
using Simcag.AIService.Domain.Entities;
using Simcag.AIService.Domain.Services;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;

namespace Simcag.AIService.Tests;

public sealed class FinancialEnrichmentOrchestratorTests
{
    private static FinancialEnrichmentOrchestrator CreateOrchestrator(
        Mock<IExpenseClassificationService> catMock,
        Mock<ISupplierExtractionService> supMock,
        Mock<IFinancialLineItemsExtractionService> lineMock,
        Mock<INameNormalizationService> normMock,
        Mock<ILogger<FinancialEnrichmentOrchestrator>> logMock,
        Mock<IEventPublisher<EnrichedFinancialDataEvent>> pubMock,
        ICategoryMatcher? categoryMatcher = null,
        ICategoryRepository? categoryRepository = null)
    {
        categoryMatcher ??= new CategoryMatcher();
        categoryRepository ??= CreateCategoryRepository();

        return new FinancialEnrichmentOrchestrator(
            catMock.Object,
            supMock.Object,
            lineMock.Object,
            normMock.Object,
            categoryMatcher,
            categoryRepository,
            logMock.Object,
            pubMock.Object);
    }

    private static ICategoryRepository CreateCategoryRepository()
    {
        var repo = new Mock<ICategoryRepository>();
        var outro = ProductCategory.Create("Outro", "Other products");
        repo.Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
                name.Equals("Outro", StringComparison.OrdinalIgnoreCase) ? outro : null);
        return repo.Object;
    }
    [Fact]
    public async Task PreviewEnrichmentAsync_UsesAiLineItems_AndPassesSummaryToClassificationAndSupplier()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "doc-ai-lines",
            RawText = "pdf blob compactado",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = "x",
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

        var lineItems = new FinancialLineItemsExtractionResult(
            new List<FinancialItem>
            {
                new() { Description = "Água", Amount = 100m },
                new() { Description = "Luz", Amount = 200m }
            },
            "Março 2026",
            0.9m,
            UsedFallback: false);

        var lineMock = new Mock<IFinancialLineItemsExtractionService>();
        lineMock.Setup(x => x.ExtractAsync(raw, It.IsAny<CancellationToken>())).ReturnsAsync(lineItems);

        string? classifyOverride = null;
        var catMock = new Mock<IExpenseClassificationService>();
        catMock
            .Setup(x => x.ClassifyAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Callback<RawFinancialDataEvent, CancellationToken, string?>((_, _, o) => classifyOverride = o)
            .ReturnsAsync(new CategoryResult(Guid.NewGuid(), "Condomínio", 0.8m, "ok", false));

        string? supplierOverride = null;
        var product = new ProductExtractionResult(null, null, Array.Empty<string>(), 0.5m, true);
        var supMock = new Mock<ISupplierExtractionService>();
        supMock
            .Setup(x => x.ExtractAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Callback<RawFinancialDataEvent, CancellationToken, string?>((_, _, o) => supplierOverride = o)
            .ReturnsAsync(new SupplierExtractionResult("X", "X", null, 0.7m, false, product));

        var normMock = new Mock<INameNormalizationService>();
        var logMock = new Mock<ILogger<FinancialEnrichmentOrchestrator>>();
        var pubMock = new Mock<IEventPublisher<EnrichedFinancialDataEvent>>();
        pubMock
            .Setup(x => x.PublishAsync(It.IsAny<EnrichedFinancialDataEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orch = CreateOrchestrator(catMock, supMock, lineMock, normMock, logMock, pubMock);

        var result = await orch.PreviewEnrichmentAsync(raw, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items[0].Description.Should().Be("Água");
        result.Items[1].Amount.Should().Be(200m);

        classifyOverride.Should().NotBeNullOrWhiteSpace();
        classifyOverride.Should().Contain("Água");
        classifyOverride.Should().Contain("Título:");
        supplierOverride.Should().Be(classifyOverride);

        pubMock.Verify(
            x => x.PublishAsync(It.IsAny<EnrichedFinancialDataEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PreviewEnrichmentAsync_FallsBackToItemBuilder_WhenAiReturnsSingleUncertainLine()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "doc-fallback",
            RawText = "texto longo",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = "y",
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = new List<object> { new FinancialItem { Description = "Heurística", Amount = 50m } }
        };

        var lineItems = new FinancialLineItemsExtractionResult(
            new List<FinancialItem> { new() { Description = "Só uma", Amount = 1m } },
            null,
            0.2m,
            UsedFallback: true);

        var lineMock = new Mock<IFinancialLineItemsExtractionService>();
        lineMock.Setup(x => x.ExtractAsync(raw, It.IsAny<CancellationToken>())).ReturnsAsync(lineItems);

        var catMock = new Mock<IExpenseClassificationService>();
        catMock
            .Setup(x => x.ClassifyAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new CategoryResult(Guid.NewGuid(), "Outro", 0.5m, "fb", true));

        var product = new ProductExtractionResult(null, null, Array.Empty<string>(), 0.4m, true);
        var supMock = new Mock<ISupplierExtractionService>();
        supMock
            .Setup(x => x.ExtractAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new SupplierExtractionResult("", "", null, 0.3m, true, product));

        var normMock = new Mock<INameNormalizationService>();
        var logMock = new Mock<ILogger<FinancialEnrichmentOrchestrator>>();
        var pubMock = new Mock<IEventPublisher<EnrichedFinancialDataEvent>>();

        var orch = CreateOrchestrator(catMock, supMock, lineMock, normMock, logMock, pubMock);

        var result = await orch.PreviewEnrichmentAsync(raw, CancellationToken.None);

        result.Items.Should().ContainSingle(i => i.Description == "Heurística" && i.Amount == 50m);
    }

    [Fact]
    public async Task PreviewEnrichmentAsync_WithIngestedLines_SkipsOllamaClassifyAndSupplier()
    {
        var ingestedJson =
            """[{"description":"Gerador industrial de energia 500kVA","amount":1900000,"quantity":2,"unitPrice":950000},{"description":"Sistema completo de automacao predial inteligente","amount":1500000,"quantity":1,"unitPrice":1500000}]""";

        var raw = new RawFinancialDataEvent
        {
            DocumentId = "37aeb1bb-5c59-431f-b492-cf686ca985b9",
            RawText = "nota fiscal",
            DocumentType = "NotaFiscal",
            Source = "ingestion",
            FileHash = "abc",
            ExtractedFields = new Dictionary<string, object?>
            {
                ["ingestedLinesJson"] = ingestedJson,
                ["supplierName"] = "EMPRESA XYZ SERVICOS LTDA",
                ["supplierTaxId"] = "12345678000199",
            },
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
        };

        var lineMock = new Mock<IFinancialLineItemsExtractionService>();
        var catMock = new Mock<IExpenseClassificationService>();
        var supMock = new Mock<ISupplierExtractionService>();
        var normMock = new Mock<INameNormalizationService>();
        normMock
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => new NormalizedNameResult(name, name, 1m, false));
        var logMock = new Mock<ILogger<FinancialEnrichmentOrchestrator>>();
        var pubMock = new Mock<IEventPublisher<EnrichedFinancialDataEvent>>();

        var orch = CreateOrchestrator(catMock, supMock, lineMock, normMock, logMock, pubMock);

        var result = await orch.PreviewEnrichmentAsync(raw, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Supplier.RawName.Should().Be("EMPRESA XYZ SERVICOS LTDA");
        result.Supplier.TaxId.Should().Be("12345678000199");
        result.Category.Should().NotBeNullOrWhiteSpace();

        lineMock.Verify(x => x.ExtractAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        catMock.Verify(
            x => x.ClassifyAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
        supMock.Verify(
            x => x.ExtractAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task EnrichAsync_PublishesMetadataOnly_WithoutBenchmarkItems()
    {
        var raw = new RawFinancialDataEvent
        {
            DocumentId = "doc-publish",
            RawText = "nota",
            DocumentType = "Invoice",
            Source = "ingestion",
            FileHash = "hash",
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = new List<object>
            {
                new FinancialItem { Description = "Camera IP Full HD 2MP", Amount = 890m },
            },
        };

        var lineItems = new FinancialLineItemsExtractionResult(
            new List<FinancialItem> { new() { Description = "Camera IP Full HD 2MP", Amount = 890m } },
            null,
            0.9m,
            UsedFallback: false);

        var lineMock = new Mock<IFinancialLineItemsExtractionService>();
        lineMock.Setup(x => x.ExtractAsync(raw, It.IsAny<CancellationToken>())).ReturnsAsync(lineItems);

        var catMock = new Mock<IExpenseClassificationService>();
        catMock
            .Setup(x => x.ClassifyAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new CategoryResult(Guid.NewGuid(), "Tecnologia", 0.9m, "ok", false));

        var product = new ProductExtractionResult("Brand", "Model", Array.Empty<string>(), 0.8m, false);
        var supMock = new Mock<ISupplierExtractionService>();
        supMock
            .Setup(x => x.ExtractAsync(It.IsAny<RawFinancialDataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new SupplierExtractionResult("Fornecedor", "Fornecedor", null, 0.9m, false, product));

        var normMock = new Mock<INameNormalizationService>();
        var logMock = new Mock<ILogger<FinancialEnrichmentOrchestrator>>();
        EnrichedFinancialDataEvent? published = null;
        var pubMock = new Mock<IEventPublisher<EnrichedFinancialDataEvent>>();
        pubMock
            .Setup(x => x.PublishAsync(It.IsAny<EnrichedFinancialDataEvent>(), It.IsAny<CancellationToken>()))
            .Callback<EnrichedFinancialDataEvent, CancellationToken>((evt, _) => published = evt)
            .Returns(Task.CompletedTask);

        var orch = CreateOrchestrator(catMock, supMock, lineMock, normMock, logMock, pubMock);

        var result = await orch.EnrichAsync(raw, CancellationToken.None);

        result.Items.Should().NotBeEmpty();
        published.Should().NotBeNull();
        published!.TriggerBenchmark.Should().BeFalse();
        published.Items.Should().BeEmpty();
        pubMock.Verify(
            x => x.PublishAsync(It.IsAny<EnrichedFinancialDataEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
