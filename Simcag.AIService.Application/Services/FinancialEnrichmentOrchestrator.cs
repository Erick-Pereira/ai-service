using System.Diagnostics;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using Simcag.Shared.Messaging.Contracts;
using Simcag.Shared.Telemetry;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Orquestrador do pipeline de enriquecimento financeiro.
/// Coordena: classificação de despesa, extração de fornecedor/produto, normalização.
/// </summary>
public sealed class FinancialEnrichmentOrchestrator : IFinancialEnrichmentOrchestrator
{
    private readonly IExpenseClassificationService _classificationService;
    private readonly ISupplierExtractionService _supplierExtractionService;
    private readonly IFinancialLineItemsExtractionService _lineItemsExtractor;
    private readonly INameNormalizationService _normalizationService;
    private readonly ILogger<FinancialEnrichmentOrchestrator> _logger;
    private readonly IEventPublisher<EnrichedFinancialDataEvent> _enrichedPublisher;

    public FinancialEnrichmentOrchestrator(
        IExpenseClassificationService classificationService,
        ISupplierExtractionService supplierExtractionService,
        IFinancialLineItemsExtractionService lineItemsExtractor,
        INameNormalizationService normalizationService,
        ILogger<FinancialEnrichmentOrchestrator> logger,
        IEventPublisher<EnrichedFinancialDataEvent> enrichedPublisher)
    {
        _classificationService = classificationService;
        _supplierExtractionService = supplierExtractionService;
        _lineItemsExtractor = lineItemsExtractor;
        _normalizationService = normalizationService;
        _logger = logger;
        _enrichedPublisher = enrichedPublisher;
    }

    public Task<EnrichedFinancialDataEvent> EnrichAsync(RawFinancialDataEvent rawData, CancellationToken ct) =>
        RunPipelineAsync(rawData, publish: true, ct);

    public Task<EnrichedFinancialDataEvent> PreviewEnrichmentAsync(RawFinancialDataEvent rawData, CancellationToken ct) =>
        RunPipelineAsync(rawData, publish: false, ct);

    private async Task<EnrichedFinancialDataEvent> RunPipelineAsync(RawFinancialDataEvent rawData, bool publish, CancellationToken ct)
    {
        using var pipeline = SimcagActivitySources.Pipeline.StartActivity("ai.financial_enrichment", ActivityKind.Internal);
        pipeline?.SetTag("simcag.document_id", rawData.DocumentId);
        pipeline?.SetTag("simcag.publish", publish);

        _logger.LogInformation(
            "Starting enrichment pipeline for document {DocumentId} (publish={Publish})",
            rawData.DocumentId,
            publish);

        // 1) Extração estruturada de linhas (LLM) — alimenta classificação/fornecedor com texto mais limpo que o PDF bruto.
        FinancialLineItemsExtractionResult lineItemsResult;
        using (SimcagActivitySources.Pipeline.StartActivity("ai.line_items.extract"))
            lineItemsResult = await _lineItemsExtractor.ExtractAsync(rawData, ct);
        var structuredSummary = BuildLineItemsSummary(lineItemsResult);
        var promptOverride = ShouldUseStructuredPrompt(lineItemsResult, structuredSummary)
            ? structuredSummary
            : null;

        // Sequencial: reduz pressão no Ollama.
        CategoryResult categoryResult;
        SupplierExtractionResult supplierResult;
        using (SimcagActivitySources.Pipeline.StartActivity("ai.classify"))
            categoryResult = await _classificationService.ClassifyAsync(rawData, ct, promptOverride);
        using (SimcagActivitySources.Pipeline.StartActivity("ai.supplier_extract"))
            supplierResult = await _supplierExtractionService.ExtractAsync(rawData, ct, promptOverride);

        // Normalizar nome do fornecedor (se não foi normalizado na extração)
        var normalizedSupplierName = supplierResult.NormalizedSupplierName;
        if (string.IsNullOrWhiteSpace(normalizedSupplierName) && !string.IsNullOrWhiteSpace(supplierResult.RawSupplierName))
        {
            var normalized = await _normalizationService.NormalizeAsync(supplierResult.RawSupplierName, ct);
            normalizedSupplierName = normalized.NormalizedName;
        }

        var overallConfidence = FinancialEnrichmentConfidence.ComputeOverall(categoryResult, supplierResult);
        if (overallConfidence < AiServiceEnvironment.LowConfidenceThreshold)
        {
            _logger.LogInformation(
                "Enrichment overall confidence {Confidence:F2} below threshold {Threshold:F2} for document {DocumentId}",
                overallConfidence,
                AiServiceEnvironment.LowConfidenceThreshold,
                rawData.DocumentId);
        }

        var preferAiItems = ShouldPreferAiLineItems(lineItemsResult);
        var usedFallback = categoryResult.UsedFallback
            || supplierResult.UsedFallback
            || supplierResult.Product.UsedFallback
            || (preferAiItems && lineItemsResult.UsedFallback);

        var productInfo = new ProductEnrichmentInfo
        {
            Brand = supplierResult.Product.Brand,
            Model = supplierResult.Product.Model,
            Features = supplierResult.Product.Features.ToList(),
            Confidence = supplierResult.Product.Confidence,
            UsedFallback = supplierResult.Product.UsedFallback
        };

        var items = preferAiItems
            ? lineItemsResult.Items.ToList()
            : FinancialEnrichmentItemBuilder.Build(rawData, supplierResult).ToList();

        items = items.Select(FinancialLineItemSemanticNormalizer.NormalizeFinancialItem).ToList();

        if (preferAiItems)
        {
            _logger.LogInformation(
                "Usando {Count} item(ns) extraído(s) por LLM para documento {DocumentId}",
                items.Count,
                rawData.DocumentId);
        }

        var enrichedEvent = new EnrichedFinancialDataEvent
        {
            DocumentId = rawData.DocumentId,
            TenantId = rawData.TenantId ?? string.Empty,
            ExpenseId = Guid.NewGuid().ToString(),
            Category = categoryResult.CategoryName,
            CategoryConfidence = categoryResult.Confidence,
            Supplier = new SupplierInfo
            {
                RawName = supplierResult.RawSupplierName,
                NormalizedName = normalizedSupplierName ?? string.Empty,
                TaxId = supplierResult.TaxId,
                Confidence = supplierResult.Confidence
            },
            Product = productInfo,
            Items = items,
            OverallConfidence = overallConfidence,
            UsedFallback = usedFallback,
            EnrichedAt = DateTime.UtcNow,
            SourceEventId = rawData.EventId.ToString()
        };

        if (publish)
        {
            using (SimcagActivitySources.Messaging.StartActivity("rabbitmq.publish.enriched"))
                await _enrichedPublisher.PublishAsync(enrichedEvent, ct);
        }

        _logger.LogInformation(
            "Enrichment completed for document {DocumentId}: Category={Category}, Supplier={Supplier}, Items={ItemCount}, Confidence={Confidence:F2}, Published={Published}",
            rawData.DocumentId, categoryResult.CategoryName, normalizedSupplierName, items.Count, overallConfidence, publish);

        return enrichedEvent;
    }

    private static string BuildLineItemsSummary(FinancialLineItemsExtractionResult r)
    {
        if (r.Items.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(r.DocumentTitle))
            sb.AppendLine($"Título: {r.DocumentTitle.Trim()}");
        foreach (var i in r.Items)
            sb.AppendLine($"{i.Description}\t{i.Amount.ToString(CultureInfo.InvariantCulture)}");
        return sb.ToString().Trim();
    }

    private static bool ShouldUseStructuredPrompt(FinancialLineItemsExtractionResult lineItems, string summary) =>
        lineItems.Items.Count > 0 && !string.IsNullOrWhiteSpace(summary);

    private static bool ShouldPreferAiLineItems(FinancialLineItemsExtractionResult r) =>
        r.Items.Count > 0 && (!r.UsedFallback || r.Items.Count >= 2 || r.Confidence >= 0.45m);
}
