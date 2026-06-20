using System.Diagnostics;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Services;
using Simcag.AIService.Domain.ValueObjects;
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
    private const decimal IngestionSupplierConfidence = 0.85m;
    private const decimal IngestionCategoryConfidence = 0.85m;

    private readonly IExpenseClassificationService _classificationService;
    private readonly ISupplierExtractionService _supplierExtractionService;
    private readonly IFinancialLineItemsExtractionService _lineItemsExtractor;
    private readonly INameNormalizationService _normalizationService;
    private readonly ICategoryMatcher _categoryMatcher;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ILogger<FinancialEnrichmentOrchestrator> _logger;
    private readonly IEventPublisher<EnrichedFinancialDataEvent> _enrichedPublisher;

    public FinancialEnrichmentOrchestrator(
        IExpenseClassificationService classificationService,
        ISupplierExtractionService supplierExtractionService,
        IFinancialLineItemsExtractionService lineItemsExtractor,
        INameNormalizationService normalizationService,
        ICategoryMatcher categoryMatcher,
        ICategoryRepository categoryRepository,
        ILogger<FinancialEnrichmentOrchestrator> logger,
        IEventPublisher<EnrichedFinancialDataEvent> enrichedPublisher)
    {
        _classificationService = classificationService;
        _supplierExtractionService = supplierExtractionService;
        _lineItemsExtractor = lineItemsExtractor;
        _normalizationService = normalizationService;
        _categoryMatcher = categoryMatcher;
        _categoryRepository = categoryRepository;
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

        // 1) Extração estruturada de linhas — fast-path quando ingestão já parseou a NF.
        FinancialLineItemsExtractionResult lineItemsResult;
        var usedIngestionLines = TryBuildLineItemsFromIngestion(rawData, out var ingestedFastPath);
        if (usedIngestionLines)
        {
            lineItemsResult = ingestedFastPath;
            _logger.LogInformation(
                "Fast-path: usando {Count} linha(s) da ingestão para documento {DocumentId} (skip LLM line-items)",
                lineItemsResult.Items.Count,
                rawData.DocumentId);
        }
        else
        {
            using (SimcagActivitySources.Pipeline.StartActivity("ai.line_items.extract"))
                lineItemsResult = await _lineItemsExtractor.ExtractAsync(rawData, ct);
        }
        var structuredSummary = BuildLineItemsSummary(lineItemsResult);
        var promptOverride = ShouldUseStructuredPrompt(lineItemsResult, structuredSummary)
            ? structuredSummary
            : null;

        CategoryResult categoryResult;
        SupplierExtractionResult supplierResult;
        if (usedIngestionLines)
        {
            _logger.LogInformation(
                "Fast-path: classificação e fornecedor por regras/ingestão para documento {DocumentId} (skip Ollama classify+supplier)",
                rawData.DocumentId);
            categoryResult = await ClassifyFromIngestionRulesAsync(lineItemsResult, structuredSummary, ct);
            supplierResult = await ExtractSupplierFromIngestionAsync(rawData, ct);
        }
        else
        {
            // Sequencial: reduz pressão no Ollama.
            using (SimcagActivitySources.Pipeline.StartActivity("ai.classify"))
                categoryResult = await _classificationService.ClassifyAsync(rawData, ct, promptOverride);
            using (SimcagActivitySources.Pipeline.StartActivity("ai.supplier_extract"))
                supplierResult = await _supplierExtractionService.ExtractAsync(rawData, ct, promptOverride);
        }

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
        items = IngestedLinesItemEnricher.Enrich(items, rawData).ToList();

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
            NotifyUserId = rawData.UploadedBy == Guid.Empty ? null : rawData.UploadedBy,
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
            var publishPayload = new EnrichedFinancialDataEvent
            {
                DocumentId = enrichedEvent.DocumentId,
                TenantId = enrichedEvent.TenantId,
                NotifyUserId = enrichedEvent.NotifyUserId,
                ExpenseId = enrichedEvent.ExpenseId,
                Category = enrichedEvent.Category,
                CategoryConfidence = enrichedEvent.CategoryConfidence,
                Supplier = enrichedEvent.Supplier,
                Product = enrichedEvent.Product,
                Items = [],
                OverallConfidence = enrichedEvent.OverallConfidence,
                UsedFallback = enrichedEvent.UsedFallback,
                EnrichedAt = enrichedEvent.EnrichedAt,
                SourceEventId = enrichedEvent.SourceEventId,
                TriggerBenchmark = false,
            };
            using (SimcagActivitySources.Messaging.StartActivity("rabbitmq.publish.enriched"))
                await _enrichedPublisher.PublishAsync(publishPayload, ct);
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

    private async Task<CategoryResult> ClassifyFromIngestionRulesAsync(
        FinancialLineItemsExtractionResult lineItems,
        string structuredSummary,
        CancellationToken ct)
    {
        var description = !string.IsNullOrWhiteSpace(structuredSummary)
            ? structuredSummary
            : string.Join(" ", lineItems.Items.Select(i => i.Description).Where(d => !string.IsNullOrWhiteSpace(d)));

        if (string.IsNullOrWhiteSpace(description))
            description = "despesa";

        var matched = _categoryMatcher.MatchCategory(description);
        var entity = await _categoryRepository.GetByNameAsync(matched.Value, ct)
                     ?? await _categoryRepository.GetByNameAsync("Outro", ct);

        return new CategoryResult(
            entity?.Id ?? Guid.Empty,
            matched.Value,
            IngestionCategoryConfidence,
            "Rule-based classification from ingested line items",
            UsedFallback: true);
    }

    private async Task<SupplierExtractionResult> ExtractSupplierFromIngestionAsync(
        RawFinancialDataEvent raw,
        CancellationToken ct)
    {
        var emptyProduct = new ProductExtractionResult(null, null, Array.Empty<string>(), 0m, true);
        var rawName = GetExtractedString(raw, "supplierName", "SupplierName");
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return new SupplierExtractionResult(
                string.Empty,
                string.Empty,
                null,
                0.3m,
                true,
                emptyProduct);
        }

        rawName = rawName.Trim();
        var taxId = GetExtractedString(raw, "supplierTaxId", "SupplierTaxId");
        var normalized = rawName;
        try
        {
            var norm = await _normalizationService.NormalizeAsync(rawName, ct);
            if (!string.IsNullOrWhiteSpace(norm.NormalizedName))
                normalized = norm.NormalizedName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Normalização do fornecedor da ingestão falhou; usando nome bruto");
        }

        return new SupplierExtractionResult(
            rawName,
            normalized,
            string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim(),
            IngestionSupplierConfidence,
            false,
            emptyProduct);
    }

    private static string? GetExtractedString(RawFinancialDataEvent raw, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!raw.ExtractedFields.TryGetValue(key, out var value) || value is null)
                continue;

            var text = value switch
            {
                string s => s,
                System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.String => el.GetString(),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static bool TryBuildLineItemsFromIngestion(
        RawFinancialDataEvent raw,
        out FinancialLineItemsExtractionResult result)
    {
        result = new FinancialLineItemsExtractionResult(Array.Empty<FinancialItem>(), null, 0m, true);
        if (!raw.ExtractedFields.TryGetValue("ingestedLinesJson", out var jsonObj))
            return false;

        var json = jsonObj switch
        {
            string s => s,
            System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.String => el.GetString() ?? string.Empty,
            _ => System.Text.Json.JsonSerializer.Serialize(jsonObj),
        };

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var lines = System.Text.Json.JsonSerializer.Deserialize<List<IngestedExpenseLineDto>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (lines is null || lines.Count == 0)
                return false;

            var items = lines.Select(l => new FinancialItem
            {
                Description = l.Description ?? string.Empty,
                Amount = l.Amount,
                Quantity = l.Quantity is > 0m ? (int)Math.Round(l.Quantity.Value, MidpointRounding.AwayFromZero) : null,
                UnitPrice = l.UnitPrice,
                ItemCode = l.ItemCode,
            }).ToList();

            result = new FinancialLineItemsExtractionResult(items, null, 0.85m, false);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private sealed class IngestedExpenseLineDto
    {
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? ItemCode { get; set; }
    }
}
