using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Entities;

namespace Simcag.AIService.Api;

public class MockCategoryRepository : ICategoryRepository
{
    private readonly List<ProductCategory> _categories = new()
    {
        ProductCategory.Create("Notebook", "Laptops and portable computers"),
        ProductCategory.Create("Monitor", "Displays and screens"),
        ProductCategory.Create("Periférico", "Peripherals like mouse, keyboard, webcam"),
        ProductCategory.Create("Hardware", "Computer components"),
        ProductCategory.Create("Software", "Applications and licenses"),
        ProductCategory.Create("Outro", "Other products")
    };

    public Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_categories.FirstOrDefault(c => c.Id == id));

    public Task<ProductCategory?> GetByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(_categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    public Task<IEnumerable<ProductCategory>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IEnumerable<ProductCategory>>(_categories);

    public Task<IEnumerable<ProductCategory>> GetActiveAsync(CancellationToken ct)
        => Task.FromResult<IEnumerable<ProductCategory>>(_categories.Where(c => c.IsActive));

    public Task AddAsync(ProductCategory category, CancellationToken ct)
    {
        _categories.Add(category);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProductCategory category, CancellationToken ct) => Task.CompletedTask;
}

public class MockAIProcessingResultRepository : IAIProcessingResultRepository
{
    private readonly List<AIProcessingResult> _results = new();

    public Task AddAsync(AIProcessingResult result, CancellationToken ct)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<AIProcessingResult>> GetByOperationTypeAsync(string operationType, int limit, CancellationToken ct)
        => Task.FromResult<IEnumerable<AIProcessingResult>>(
            _results.Where(r => r.OperationType == operationType).Take(limit));

    public Task<IEnumerable<AIProcessingResult>> GetRecentAsync(int hours, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        return Task.FromResult<IEnumerable<AIProcessingResult>>(
            _results.Where(r => r.ProcessedAt >= cutoff));
    }
}