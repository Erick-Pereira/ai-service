using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Utilities;
using Simcag.AIService.Domain.Entities;
using Simcag.AIService.Domain.Services;
using Simcag.AIService.Domain.ValueObjects;
using Simcag.Shared.Events;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Services;

/// <summary>
/// Serviço de classificação de categoria de despesa utilizando IA com fallback para regras.
/// </summary>
public sealed class ExpenseClassificationService : IExpenseClassificationService
{
    private readonly IOllamaClient _ollama;
    private readonly ILogger<ExpenseClassificationService> _logger;
    private readonly ICategoryRepository _categoryRepo;
    private readonly ICategoryMatcher _categoryMatcher;
    private readonly ICategoryResponseExtractor _responseExtractor;
    private readonly IConfidenceCalculator _confidenceCalculator;
    private readonly string _modelName;
    private readonly IAiInferenceCache _inferenceCache;
    private readonly TimeSpan _inferenceTtl;

    public ExpenseClassificationService(
        IOllamaClient ollama,
        ILogger<ExpenseClassificationService> logger,
        ICategoryRepository categoryRepo,
        ICategoryMatcher categoryMatcher,
        ICategoryResponseExtractor responseExtractor,
        IConfidenceCalculator confidenceCalculator,
        IAiInferenceCache inferenceCache)
    {
        _ollama = ollama;
        _logger = logger;
        _categoryRepo = categoryRepo;
        _categoryMatcher = categoryMatcher;
        _responseExtractor = responseExtractor;
        _confidenceCalculator = confidenceCalculator;
        _inferenceCache = inferenceCache;

        _modelName = AiServiceEnvironment.ModelName;
        _inferenceTtl = AiServiceEnvironment.InferenceCacheTtl;
    }

    private const int MaxClassificationTextChars = 8000;

    public async Task<CategoryResult> ClassifyAsync(RawFinancialDataEvent financialData, CancellationToken ct, string? classificationTextOverride = null)
    {
        var description = string.IsNullOrWhiteSpace(classificationTextOverride)
            ? financialData.RawText
            : classificationTextOverride;
        description = TruncateForPrompt(description ?? string.Empty, MaxClassificationTextChars);

        // 1. Tentar IA
        if (await _ollama.IsAvailableAsync(ct))
        {
            try
            {
                var activeCategories = await _categoryRepo.GetActiveAsync(ct);
                var categories = (activeCategories ?? Enumerable.Empty<ProductCategory>())
                    .Select(c => c.Name.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var prompt = BuildExpenseClassificationPrompt(description, categories);
                var rawResponse = await GenerateWithCacheAsync(prompt, ct);

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    var extractedCategory = TryExtractCategoryFromStructuredJson(rawResponse, categories)
                                            ?? _responseExtractor.Extract(rawResponse);
                    var confidence = _confidenceCalculator.Calculate(rawResponse, extractedCategory);

                    var categoryEntity = await _categoryRepo.GetByNameAsync(extractedCategory.Value, ct);
                    if (categoryEntity != null)
                    {
                        return new CategoryResult(
                            categoryEntity.Id,
                            categoryEntity.Name.Value,
                            confidence.Value,
                            $"AI classified as {extractedCategory.Value}",
                            UsedFallback: false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI expense classification failed, using fallback");
            }
        }

        // 2. Fallback: regras baseadas em keywords
        var fallbackCategory = _categoryMatcher.MatchCategory(description);
        var fallbackConfidence = new ConfidenceScore(0.7m);
        var fallbackEntity = await _categoryRepo.GetByNameAsync(fallbackCategory.Value, ct)
                          ?? await _categoryRepo.GetByNameAsync("Outro", ct);

        return new CategoryResult(
            fallbackEntity?.Id ?? Guid.Empty,
            fallbackCategory.Value,
            fallbackConfidence.Value,
            "Rule-based classification fallback",
            UsedFallback: true);
    }

    private static string BuildExpenseClassificationPrompt(string rawText, IReadOnlyCollection<string> categories)
    {
        var categoryList = categories.Count > 0
            ? string.Join(", ", categories)
            : "Outro";

        return
            $"Classify this financial expense into one of these categories: {categoryList}. " +
            $"Expense description: {rawText}. " +
            $"Respond with only the category name.";
    }

    private static CategoryName? TryExtractCategoryFromStructuredJson(string rawResponse, IReadOnlyCollection<string> categories)
    {
        if (!LlmStructuredJsonParser.TryParseJsonObject(rawResponse, "expense_category_json", out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            string? cat = null;
            if (LlmStructuredJsonParser.TryGetStringProperty(root, "category", out cat)
                || LlmStructuredJsonParser.TryGetStringProperty(root, "Category", out cat))
            {
                var trimmed = cat.Trim();
                if (categories.Any(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return new CategoryName(trimmed);
            }
        }

        return null;
    }

    private async Task<string> GenerateWithCacheAsync(string prompt, CancellationToken ct)
    {
        var key = LlmInferenceCacheKeys.ForPrompt("expense-category", _modelName, prompt);
        var cached = await _inferenceCache.GetAsync(key, ct);
        if (!string.IsNullOrWhiteSpace(cached))
            return cached;

        var response = await _ollama.GenerateCompletionAsync(prompt, _modelName, ct);
        if (!string.IsNullOrWhiteSpace(response))
            await _inferenceCache.SetAsync(key, response, _inferenceTtl, ct);

        return response;
    }

    private static string TruncateForPrompt(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars];
    }

}
