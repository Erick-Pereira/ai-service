using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Entities;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Services;

public class AIService : IAIService
{
    private readonly IOllamaClient _ollamaClient;
    private readonly ILogger<AIService> _logger;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAIProcessingResultRepository _processingResultRepository;
    private readonly IEventPublisher<NormalizedDataEvent> _normalizedEventPublisher;
    private readonly IEventPublisher<CategorizedDataEvent> _categorizedEventPublisher;

    public AIService(
        IOllamaClient ollamaClient,
        ILogger<AIService> logger,
        ICategoryRepository categoryRepository,
        IAIProcessingResultRepository processingResultRepository,
        IEventPublisher<NormalizedDataEvent> normalizedEventPublisher,
        IEventPublisher<CategorizedDataEvent> categorizedEventPublisher)
    {
        _ollamaClient = ollamaClient;
        _logger = logger;
        _categoryRepository = categoryRepository;
        _processingResultRepository = processingResultRepository;
        _normalizedEventPublisher = normalizedEventPublisher;
        _categorizedEventPublisher = categorizedEventPublisher;
    }

    private static readonly Dictionary<string, string> _categoryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "notebook", "Notebook" },
        { "laptop", "Notebook" },
        { "macbook", "Notebook" },
        { "monitor", "Monitor" },
        { "display", "Monitor" },
        { "screen", "Monitor" },
        { "mouse", "Periférico" },
        { "teclado", "Periférico" },
        { "keyboard", "Periférico" },
        { "headset", "Periférico" },
        { "webcam", "Periférico" },
        { "cpu", "Hardware" },
        { "processador", "Hardware" },
        { "processor", "Hardware" },
        { "gpu", "Hardware" },
        { "placa de vídeo", "Hardware" },
        { "memória", "Hardware" },
        { "memory", "Hardware" },
        { "ssd", "Hardware" },
        { "hdd", "Hardware" },
        { "disco", "Hardware" },
        { "software", "Software" },
        { "licença", "Software" },
        { "license", "Software" },
        { "assinatura", "Software" },
        { "subscription", "Software" }
    };

    public async Task<CategoryResult> CategorizeProductAsync(string productDescription, CancellationToken ct)
    {
        var inputText = productDescription;
        var processedText = productDescription;
        var usedFallback = false;
        var aiModel = "llama3.1";
        decimal confidence = 0;
        string resultData = "{}";
        string rawResponse = "";

        try
        {
            // Try AI-powered categorization first
            if (await _ollamaClient.IsAvailableAsync(ct))
            {
                var prompt = $"Categorize this product into one of these categories: Notebook, Monitor, Periférico, Hardware, Software, Outro. " +
                           $"Product: {productDescription}. " +
                           $"Respond with only the category name.";

                rawResponse = await _ollamaClient.GenerateCompletionAsync(prompt, "llama3.1", ct);
                var categoryName = ExtractCategoryFromResponse(rawResponse);

                if (!string.IsNullOrEmpty(categoryName))
                {
                    confidence = CalculateConfidence(rawResponse, categoryName);
                    var category = await _categoryRepository.GetByNameAsync(categoryName, ct);

                    if (category != null)
                    {
                        resultData = JsonSerializer.Serialize(new { categoryId = category.Id, categoryName = category.Name });
                        var result = new CategoryResult
                        {
                            CategoryId = category.Id,
                            CategoryName = category.Name,
                            Confidence = confidence,
                            Reasoning = $"AI categorized as {categoryName} with {confidence:F2} confidence",
                            UsedFallback = false
                        };

                        await SaveProcessingResult(inputText, processedText, "categorization", confidence, aiModel, resultData, rawResponse, false, ct);
                        return result;
                    }
                }
            }

            // Fallback to rule-based categorization
            usedFallback = true;
            var fallbackCategory = GetFallbackCategory(productDescription);
            confidence = 0.7m; // Lower confidence for rule-based

            var fallbackCategoryEntity = await _categoryRepository.GetByNameAsync(fallbackCategory, ct) ??
                                        await _categoryRepository.GetByNameAsync("Outro", ct);

            var fallbackResult = new CategoryResult
            {
                CategoryId = fallbackCategoryEntity?.Id ?? Guid.Empty,
                CategoryName = fallbackCategory,
                Confidence = confidence,
                Reasoning = $"Rule-based categorization: {fallbackCategory}",
                UsedFallback = true
            };

            resultData = JsonSerializer.Serialize(new { categoryName = fallbackCategory, method = "rule-based" });
            await SaveProcessingResult(inputText, processedText, "categorization", confidence, "rule-based", resultData, "", true, ct);

            _logger.LogInformation("Used fallback categorization for product: {Product}", productDescription);
            return fallbackResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to categorize product: {Product}", productDescription);

            // Ultimate fallback
            var defaultCategory = await _categoryRepository.GetByNameAsync("Outro", ct);
            return new CategoryResult
            {
                CategoryId = defaultCategory?.Id ?? Guid.Empty,
                CategoryName = "Outro",
                Confidence = 0.1m,
                Reasoning = "Error occurred, using default category",
                UsedFallback = true
            };
        }
    }

    public async Task<EntityExtractionResult> ExtractEntitiesAsync(string productDescription, CancellationToken ct)
    {
        try
        {
            if (await _ollamaClient.IsAvailableAsync(ct))
            {
                var prompt = $"Extract brand, model, and key features from this product description: {productDescription}. " +
                           $"Respond in JSON format: {{\"brand\": \"string\", \"model\": \"string\", \"features\": [\"feature1\", \"feature2\"]}}";

                var rawResponse = await _ollamaClient.GenerateCompletionAsync(prompt, "llama3.1", ct);

                try
                {
                    var extracted = JsonSerializer.Deserialize<EntityExtractionResult>(rawResponse);
                    if (extracted != null)
                    {
                        extracted.Confidence = 0.85m;
                        extracted.UsedFallback = false;
                        return extracted;
                    }
                }
                catch (JsonException)
                {
                    // JSON parsing failed, use fallback
                }
            }

            // Fallback to simple regex-based extraction
            return ExtractEntitiesFallback(productDescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract entities from: {Product}", productDescription);
            return new EntityExtractionResult
            {
                Brand = "",
                Model = "",
                Features = new List<string>(),
                Confidence = 0.1m,
                UsedFallback = true
            };
        }
    }

    public async Task<StandardizationResult> StandardizeNameAsync(string productName, CancellationToken ct)
    {
        try
        {
            if (await _ollamaClient.IsAvailableAsync(ct))
            {
                var prompt = $"Standardize this product name to a clean, consistent format: {productName}. " +
                           $"Remove extra spaces, fix casing, and make it professional. Respond with only the standardized name.";

                var standardizedName = await _ollamaClient.GenerateCompletionAsync(prompt, "llama3.1", ct);

                if (!string.IsNullOrWhiteSpace(standardizedName))
                {
                    return new StandardizationResult
                    {
                        StandardizedName = standardizedName.Trim(),
                        OriginalName = productName,
                        Confidence = 0.9m,
                        UsedFallback = false
                    };
                }
            }

            // Fallback to simple text processing
            var standardized = productName
                .Trim()
                .Replace("  ", " ") // Remove double spaces
                .ToTitleCase(); // Simple title case

            return new StandardizationResult
            {
                StandardizedName = standardized,
                OriginalName = productName,
                Confidence = 0.6m,
                UsedFallback = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to standardize name: {Product}", productName);
            return new StandardizationResult
            {
                StandardizedName = productName,
                OriginalName = productName,
                Confidence = 0.1m,
                UsedFallback = true
            };
        }
    }

    private string ExtractCategoryFromResponse(string response)
    {
        var categories = new[] { "Notebook", "Monitor", "Periférico", "Hardware", "Software", "Outro" };
        foreach (var category in categories)
        {
            if (response.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }
        return "";
    }

    private decimal CalculateConfidence(string response, string category)
    {
        // Simple confidence calculation based on response clarity
        if (response.Length < 50 && response.Contains(category))
            return 0.9m;
        else if (response.Contains(category))
            return 0.7m;
        else
            return 0.4m;
    }

    private string GetFallbackCategory(string productDescription)
    {
        foreach (var (keyword, category) in _categoryKeywords)
        {
            if (productDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }
        return "Outro";
    }

    private EntityExtractionResult ExtractEntitiesFallback(string productDescription)
    {
        // Simple regex-based extraction (could be improved with more sophisticated logic)
        var brand = "";
        var model = "";
        var features = new List<string>();

        // Look for common brand patterns
        var commonBrands = new[] { "Dell", "HP", "Lenovo", "Apple", "Samsung", "LG", "ASUS", "Acer" };
        foreach (var b in commonBrands)
        {
            if (productDescription.Contains(b, StringComparison.OrdinalIgnoreCase))
            {
                brand = b;
                break;
            }
        }

        return new EntityExtractionResult
        {
            Brand = brand,
            Model = model,
            Features = features,
            Confidence = 0.5m,
            UsedFallback = true
        };
    }

    public async Task ProcessRawDataAsync(PriceCollectedEvent rawDataEvent, CancellationToken ct)
    {
        using var scope = _logger.BeginScope("{ProductId} {EventId}", rawDataEvent.ProductId, rawDataEvent.EventId);

        _logger.LogInformation("Starting AI processing for raw data event: {ProductId}", rawDataEvent.ProductId);

        try
        {
            // Perform all AI operations in parallel for better performance
            var categorizationTask = CategorizeProductAsync(rawDataEvent.ProductName, ct);
            var extractionTask = ExtractEntitiesAsync(rawDataEvent.ProductName, ct);
            var standardizationTask = StandardizeNameAsync(rawDataEvent.ProductName, ct);

            await Task.WhenAll(categorizationTask, extractionTask, standardizationTask);

            var categoryResult = await categorizationTask;
            var extractionResult = await extractionTask;
            var standardizationResult = await standardizationTask;

            // Create normalized data event
            var normalizedEvent = new NormalizedDataEvent
            {
                OriginalProductId = rawDataEvent.ProductId,
                NormalizedProductId = rawDataEvent.ProductId, // Could be enhanced with deduplication logic
                NormalizedName = standardizationResult.StandardizedName,
                Category = categoryResult.CategoryName,
                Confidence = Math.Min(categoryResult.Confidence, standardizationResult.Confidence),
                UsedFallback = categoryResult.UsedFallback || standardizationResult.UsedFallback,
                ProcessedAt = DateTime.UtcNow
            };

            // Publish normalized event to RabbitMQ
            await _normalizedEventPublisher.PublishAsync(normalizedEvent, ct);

            // Create categorized data event with extracted entities
            var categorizedEvent = new CategorizedDataEvent
            {
                ProductId = rawDataEvent.ProductId,
                ProductName = standardizationResult.StandardizedName,
                Category = categoryResult.CategoryName,
                Confidence = categoryResult.Confidence,
                ExtractedEntities = new Dictionary<string, string>
                {
                    ["brand"] = extractionResult.Brand,
                    ["model"] = extractionResult.Model,
                    ["features"] = string.Join(", ", extractionResult.Features)
                },
                CategorizedAt = DateTime.UtcNow
            };

            // Publish categorized event to RabbitMQ
            await _categorizedEventPublisher.PublishAsync(categorizedEvent, ct);

            _logger.LogInformation("AI processing completed for product {ProductId}: Category={Category}, Confidence={Confidence:F2}",
                rawDataEvent.ProductId, categoryResult.CategoryName, normalizedEvent.Confidence);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process raw data event for product {ProductId}", rawDataEvent.ProductId);
            throw;
        }
    }

    private async Task SaveProcessingResult(
        string inputText,
        string processedText,
        string operationType,
        decimal confidence,
        string aiModel,
        string resultData,
        string rawResponse,
        bool usedFallback,
        CancellationToken ct)
    {
        try
        {
            var result = AIProcessingResult.Create(
                inputText,
                processedText,
                operationType,
                confidence,
                aiModel,
                resultData,
                rawResponse,
                usedFallback);

            await _processingResultRepository.AddAsync(result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save AI processing result");
        }
    }
}

// Extension method for title case
public static class StringExtensions
{
    public static string ToTitleCase(this string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i][1..].ToLower();
            }
        }
        return string.Join(' ', words);
    }
}