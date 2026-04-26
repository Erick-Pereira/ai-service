using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório em memória para ProductCategory (desenvolvimento/protótipo).
/// Em produção, substituir por EF Core repository com PostgreSQL.
/// </summary>
public sealed class InMemoryCategoryRepository : ICategoryRepository
{
    private readonly ConcurrentDictionary<Guid, ProductCategory> _categories = new();
    private readonly ILogger<InMemoryCategoryRepository> _logger;

    public InMemoryCategoryRepository(ILogger<InMemoryCategoryRepository> logger)
    {
        _logger = logger;

        // Seed com categorias padrão
        var defaultCategories = new[]
        {
            ProductCategory.Create("Notebook", "Laptops and portable computers"),
            ProductCategory.Create("Monitor", "Displays and screens"),
            ProductCategory.Create("Periférico", "Peripherals like mouse, keyboard, webcam"),
            ProductCategory.Create("Hardware", "Computer components"),
            ProductCategory.Create("Software", "Applications and licenses"),
            ProductCategory.Create("Outro", "Other products")
        };

        foreach (var cat in defaultCategories)
        {
            _categories[cat.Id] = cat;
        }

        _logger.LogInformation("Initialized InMemoryCategoryRepository with {Count} default categories", _categories.Count);
    }

    public Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_categories.TryGetValue(id, out var cat) ? cat : null);

    public Task<ProductCategory?> GetByNameAsync(string name, CancellationToken ct)
    {
        var cat = _categories.Values.FirstOrDefault(c => c.Name.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(cat);
    }

    public Task<IEnumerable<ProductCategory>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IEnumerable<ProductCategory>>(_categories.Values.ToList());

    public Task<IEnumerable<ProductCategory>> GetActiveAsync(CancellationToken ct)
        => Task.FromResult<IEnumerable<ProductCategory>>(_categories.Values.Where(c => c.IsActive).ToList());

    public Task AddAsync(ProductCategory category, CancellationToken ct)
    {
        _categories[category.Id] = category;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProductCategory category, CancellationToken ct)
    {
        if (_categories.ContainsKey(category.Id))
            _categories[category.Id] = category;
        return Task.CompletedTask;
    }
}
