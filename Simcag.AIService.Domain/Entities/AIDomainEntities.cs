using Simcag.Shared.Common;
using System.Text.Json.Serialization;

namespace Simcag.AIService.Domain.Entities;

public class ProductCategory : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation property
    public ProductCategory? ParentCategory { get; private set; }
    public ICollection<ProductCategory> SubCategories { get; private set; } = new List<ProductCategory>();

    private ProductCategory() { } // EF Core

    private ProductCategory(string name, string description, Guid? parentCategoryId)
    {
        Name = name;
        Description = description;
        ParentCategoryId = parentCategoryId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public static ProductCategory Create(string name, string description, Guid? parentCategoryId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required", nameof(name));

        return new ProductCategory(name, description, parentCategoryId);
    }

    public void Update(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required", nameof(name));

        Name = name;
        Description = description;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}

public class AIProcessingResult : BaseEntity
{
    public string InputText { get; private set; } = string.Empty;
    public string ProcessedText { get; private set; } = string.Empty;
    public string OperationType { get; private set; } = string.Empty; // "categorization", "extraction", "standardization"
    public decimal ConfidenceScore { get; private set; }
    public string AIModel { get; private set; } = string.Empty;
    public DateTime ProcessedAt { get; private set; }
    public bool UsedFallback { get; private set; }

    // Results as JSON strings
    public string ResultData { get; private set; } = string.Empty; // JSON result
    public string RawAIResponse { get; private set; } = string.Empty; // Raw LLM response

    private AIProcessingResult() { } // EF Core

    private AIProcessingResult(
        string inputText,
        string processedText,
        string operationType,
        decimal confidenceScore,
        string aiModel,
        string resultData,
        string rawAIResponse,
        bool usedFallback)
    {
        InputText = inputText;
        ProcessedText = processedText;
        OperationType = operationType;
        ConfidenceScore = confidenceScore;
        AIModel = aiModel;
        ResultData = resultData;
        RawAIResponse = rawAIResponse;
        UsedFallback = usedFallback;
        ProcessedAt = DateTime.UtcNow;
    }

    public static AIProcessingResult Create(
        string inputText,
        string processedText,
        string operationType,
        decimal confidenceScore,
        string aiModel,
        string resultData,
        string rawAIResponse,
        bool usedFallback)
    {
        if (string.IsNullOrWhiteSpace(inputText))
            throw new ArgumentException("Input text is required", nameof(inputText));
        if (string.IsNullOrWhiteSpace(operationType))
            throw new ArgumentException("Operation type is required", nameof(operationType));

        return new AIProcessingResult(
            inputText,
            processedText,
            operationType,
            confidenceScore,
            aiModel,
            resultData,
            rawAIResponse,
            usedFallback);
    }
}

public class CategoryPrediction
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}