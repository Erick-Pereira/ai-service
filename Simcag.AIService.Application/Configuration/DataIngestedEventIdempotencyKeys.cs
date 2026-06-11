using Simcag.Shared.Events;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Chave estável para idempotência de processamento de <see cref="DataIngestedEvent"/>.
/// Prioriza <see cref="DataIngestedEvent.DocumentId"/> para não bloquear re-ingestões
/// (<c>Force=true</c>) do mesmo ficheiro com novo identificador de documento.
/// </summary>
public static class DataIngestedEventIdempotencyKeys
{
    public static string Build(DataIngestedEvent e)
    {
        if (e.DocumentId != Guid.Empty)
            return $"ai-service:ingested-doc:{e.DocumentId}";

        if (e.EventId != Guid.Empty)
            return $"ai-service:ingested-event:{e.EventId}";

        if (!string.IsNullOrWhiteSpace(e.FileHash))
            return $"ai-service:ingested-file:{e.FileHash}:{e.TenantId}";

        return $"ai-service:ingested-unknown:{Guid.NewGuid():N}";
    }
}
