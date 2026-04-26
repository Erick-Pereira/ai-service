namespace Simcag.AIService.Api.Controllers.Financial;

public sealed record AiServiceCapabilitiesResponse(
    string Service,
    string ConfiguredModel,
    double IdempotencyTtlHours,
    double InferenceCacheTtlHours,
    double SupplierNormalizationTtlHours,
    string? EventsExchangeFromEnv,
    string ResolvedEventsExchange);
