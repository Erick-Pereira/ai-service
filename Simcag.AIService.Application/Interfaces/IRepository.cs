using Simcag.AIService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Interfaces;

public interface ICategoryRepository
{
    Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<ProductCategory?> GetByNameAsync(string name, CancellationToken ct);
    Task<IEnumerable<ProductCategory>> GetAllAsync(CancellationToken ct);
    Task<IEnumerable<ProductCategory>> GetActiveAsync(CancellationToken ct);
    Task AddAsync(ProductCategory category, CancellationToken ct);
    Task UpdateAsync(ProductCategory category, CancellationToken ct);
}

public interface IAIProcessingResultRepository
{
    Task AddAsync(AIProcessingResult result, CancellationToken ct);
    Task<IEnumerable<AIProcessingResult>> GetByOperationTypeAsync(string operationType, int limit, CancellationToken ct);
    Task<IEnumerable<AIProcessingResult>> GetRecentAsync(int hours, CancellationToken ct);
}