using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Chave estável para idempotência de processamento de <see cref="DataIngestedEvent"/>.
/// </summary>
public static class DataIngestedEventIdempotencyKeys
{
    public static string Build(DataIngestedEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.FileHash))
            return $"ai-service:ingested-file:{e.FileHash}:{e.TenantId}";

        if (e.DocumentId != Guid.Empty)
            return $"ai-service:ingested-doc:{e.DocumentId}";

        return $"ai-service:ingested-event:{e.EventId}";
    }
}
