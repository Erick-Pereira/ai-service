namespace Simcag.AIService.Infrastructure.Clients;

/// <summary>Breaker simples por falhas consecutivas (processo único / coordenador).</summary>
public sealed class OllamaCircuitBreaker
{
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTime? _openUntilUtc;

    public bool IsOpen(DateTime utcNow)
    {
        lock (_sync)
        {
            if (_openUntilUtc is { } until && utcNow < until)
                return true;

            if (_openUntilUtc.HasValue && utcNow >= _openUntilUtc)
            {
                _openUntilUtc = null;
                _consecutiveFailures = 0;
            }

            return false;
        }
    }

    public void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _openUntilUtc = null;
        }
    }

    public void RecordFailure(int threshold, int openSeconds, DateTime utcNow)
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= threshold)
                _openUntilUtc = utcNow.AddSeconds(Math.Clamp(openSeconds, 1, 3600));
        }
    }
}
