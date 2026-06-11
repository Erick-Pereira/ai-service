using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Chave estável para idempotência de processamento de <see cref="RawFinancialDataEvent"/>.
/// Prioriza <see cref="RawFinancialDataEvent.DocumentId"/> — o hash sozinho bloquearia
/// reprocessamento legítimo quando o mesmo PDF gera um novo documento.
/// </summary>
public static class RawFinancialEventIdempotencyKeys
{
    public static string Build(RawFinancialDataEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.DocumentId))
            return $"ai-service:raw-doc:{e.DocumentId.Trim()}";

        if (e.EventId != Guid.Empty)
            return $"ai-service:raw-event:{e.EventId}";

        if (!string.IsNullOrWhiteSpace(e.FileHash))
            return $"ai-service:raw-file:{e.FileHash}";

        return $"ai-service:raw-unknown:{Guid.NewGuid():N}";
    }
}
