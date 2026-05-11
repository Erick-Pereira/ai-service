using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Services;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;

namespace Simcag.AIService.Tests;

public sealed class FinancialEnrichmentOrchestratorTests
{
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

        var orch = new FinancialEnrichmentOrchestrator(
            catMock.Object,
            supMock.Object,
            lineMock.Object,
            normMock.Object,
            logMock.Object,
            pubMock.Object);

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

        var orch = new FinancialEnrichmentOrchestrator(
            catMock.Object,
            supMock.Object,
            lineMock.Object,
            normMock.Object,
            logMock.Object,
            pubMock.Object);

        var result = await orch.PreviewEnrichmentAsync(raw, CancellationToken.None);

        result.Items.Should().ContainSingle(i => i.Description == "Heurística" && i.Amount == 50m);
    }
}
