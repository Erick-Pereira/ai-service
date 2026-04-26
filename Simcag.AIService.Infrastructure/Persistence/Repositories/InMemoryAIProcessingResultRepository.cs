using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório em memória para AIProcessingResult (desenvolvimento/protótipo).
/// Em produção, substituir por EF Core repository com PostgreSQL.
/// </summary>
public sealed class InMemoryAIProcessingResultRepository : IAIProcessingResultRepository
{
    private readonly ConcurrentQueue<AIProcessingResult> _results = new();
    private readonly ILogger<InMemoryAIProcessingResultRepository> _logger;

    public InMemoryAIProcessingResultRepository(ILogger<InMemoryAIProcessingResultRepository> logger)
    {
        _logger = logger;
    }

    public Task AddAsync(AIProcessingResult result, CancellationToken ct)
    {
        _results.Enqueue(result);
        _logger.LogDebug("Stored AI processing result: {OperationType} ({ResultId})", result.OperationType, result.Id);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<AIProcessingResult>> GetByOperationTypeAsync(string operationType, int limit, CancellationToken ct)
    {
        var results = _results.Where(r => r.OperationType == operationType).Take(limit).ToList();
        return Task.FromResult<IEnumerable<AIProcessingResult>>(results);
    }

    public Task<IEnumerable<AIProcessingResult>> GetRecentAsync(int hours, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var results = _results.Where(r => r.ProcessedAt >= cutoff).ToList();
        return Task.FromResult<IEnumerable<AIProcessingResult>>(results);
    }
}
