using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Chave estável para idempotência de processamento de <see cref="RawFinancialDataEvent"/>.
/// </summary>
public static class RawFinancialEventIdempotencyKeys
{
    public static string Build(RawFinancialDataEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.FileHash))
            return $"ai-service:raw-file:{e.FileHash}";

        if (e.EventId != Guid.Empty)
            return $"ai-service:raw-event:{e.EventId}";

        return $"ai-service:raw-doc:{e.DocumentId}";
    }
}
