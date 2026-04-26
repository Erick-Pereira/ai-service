using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Microsoft.Extensions.Logging;
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
    private readonly INameNormalizationService _normalizationService;
    private readonly ILogger<FinancialEnrichmentOrchestrator> _logger;
    private readonly IEventPublisher<EnrichedFinancialDataEvent> _enrichedPublisher;

    public FinancialEnrichmentOrchestrator(
        IExpenseClassificationService classificationService,
        ISupplierExtractionService supplierExtractionService,
        INameNormalizationService normalizationService,
        ILogger<FinancialEnrichmentOrchestrator> logger,
        IEventPublisher<EnrichedFinancialDataEvent> enrichedPublisher)
    {
        _classificationService = classificationService;
        _supplierExtractionService = supplierExtractionService;
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
        _logger.LogInformation(
            "Starting enrichment pipeline for document {DocumentId} (publish={Publish})",
            rawData.DocumentId,
            publish);

        // Sequencial: reduz pressão no Ollama (duas chamadas LLM em paralelo costumavam falhar intermitentemente).
        var categoryResult = await _classificationService.ClassifyAsync(rawData, ct);
        var supplierResult = await _supplierExtractionService.ExtractAsync(rawData, ct);

        // Normalizar nome do fornecedor (se não foi normalizado na extração)
        var normalizedSupplierName = supplierResult.NormalizedSupplierName;
        if (string.IsNullOrWhiteSpace(normalizedSupplierName) && !string.IsNullOrWhiteSpace(supplierResult.RawSupplierName))
        {
            var normalized = await _normalizationService.NormalizeAsync(supplierResult.RawSupplierName, ct);
            normalizedSupplierName = normalized.NormalizedName;
        }

        var overallConfidence = Math.Min(
            Math.Min(categoryResult.Confidence, supplierResult.Confidence),
            supplierResult.Product.Confidence);
        var usedFallback = categoryResult.UsedFallback
            || supplierResult.UsedFallback
            || supplierResult.Product.UsedFallback;

        var productInfo = new ProductEnrichmentInfo
        {
            Brand = supplierResult.Product.Brand,
            Model = supplierResult.Product.Model,
            Features = supplierResult.Product.Features.ToList(),
            Confidence = supplierResult.Product.Confidence,
            UsedFallback = supplierResult.Product.UsedFallback
        };

        var items = BuildFinancialItems(rawData, supplierResult);

        var enrichedEvent = new EnrichedFinancialDataEvent
        {
            DocumentId = rawData.DocumentId,
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
            await _enrichedPublisher.PublishAsync(enrichedEvent, ct);

        _logger.LogInformation(
            "Enrichment completed for document {DocumentId}: Category={Category}, Supplier={Supplier}, Items={ItemCount}, Confidence={Confidence:F2}, Published={Published}",
            rawData.DocumentId, categoryResult.CategoryName, normalizedSupplierName, items.Count, overallConfidence, publish);

        return enrichedEvent;
    }

    /// <summary>Monta linhas de item a partir do produto extraído; se vazio, usa o texto bruto compactado como uma linha descritiva.</summary>
    private static List<FinancialItem> BuildFinancialItems(RawFinancialDataEvent raw, SupplierExtractionResult s)
    {
        var items = new List<FinancialItem>();
        var p = s.Product;

        if (!string.IsNullOrWhiteSpace(p.Brand) || !string.IsNullOrWhiteSpace(p.Model))
        {
            var line = string.Join(' ', new[] { p.Brand, p.Model }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (line.Length > 0)
                items.Add(new FinancialItem { Description = line, Amount = 0m });
        }

        foreach (var f in p.Features)
        {
            if (!string.IsNullOrWhiteSpace(f))
                items.Add(new FinancialItem { Description = f.Trim(), Amount = 0m });
        }

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(raw.RawText))
        {
            var compact = raw.RawText.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (compact.Contains("  ", StringComparison.Ordinal))
                compact = compact.Replace("  ", " ", StringComparison.Ordinal);
            if (compact.Length > 512)
                compact = compact[..512];
            if (compact.Length > 0)
                items.Add(new FinancialItem { Description = compact, Amount = 0m });
        }

        return items;
    }
}
