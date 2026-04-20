using Simcag.AIService.Domain.Entities;
using Simcag.Shared.Events;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Interfaces;

public interface IAIService
{
    Task<CategoryResult> CategorizeProductAsync(string productDescription, CancellationToken ct);
    Task<EntityExtractionResult> ExtractEntitiesAsync(string productDescription, CancellationToken ct);
    Task<StandardizationResult> StandardizeNameAsync(string productName, CancellationToken ct);
    Task ProcessRawDataAsync(PriceCollectedEvent rawDataEvent, CancellationToken ct);
}

public interface IOllamaClient
{
    Task<string> GenerateCompletionAsync(string prompt, string model = "llama3.1", CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

// Result DTOs
public class CategoryResult
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public bool UsedFallback { get; set; }
}

public class EntityExtractionResult
{
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public decimal Confidence { get; set; }
    public bool UsedFallback { get; set; }
}

public class StandardizationResult
{
    public string StandardizedName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public bool UsedFallback { get; set; }
}