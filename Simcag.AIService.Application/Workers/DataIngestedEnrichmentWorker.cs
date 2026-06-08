using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Mapping;
using Simcag.AIService.Application.UseCases.Financial;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging;
using Simcag.Shared.Messaging.Contracts;
using Simcag.Shared.Messaging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Simcag.AIService.Application.Workers;

/// <summary>
/// Consome <see cref="DataIngestedEvent"/> (fila dedicada) → enriquece → publica <see cref="EnrichedFinancialDataEvent"/>.
/// </summary>
public sealed class DataIngestedEnrichmentWorker : BackgroundService
{
    private readonly IEventConsumer<DataIngestedEvent> _eventConsumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataIngestedEnrichmentWorker> _logger;
    private readonly TimeSpan _idempotencyTtl;

    public DataIngestedEnrichmentWorker(
        IEventConsumer<DataIngestedEvent> eventConsumer,
        IServiceScopeFactory scopeFactory,
        ILogger<DataIngestedEnrichmentWorker> logger)
    {
        _eventConsumer = eventConsumer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _idempotencyTtl = AiServiceEnvironment.IdempotencyTtl;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DataIngestedEnrichmentWorker started (queue {Queue}, event {Event})",
            EventNames.AiDataIngested,
            nameof(DataIngestedEvent));

        await foreach (var messageEnvelope in _eventConsumer.ReadMessagesAsync(stoppingToken))
        {
            var correlationId = Guid.NewGuid().ToString("N")[..16];
            using (MessagingConsumeTelemetry.BeginConsume(messageEnvelope, out _))
            {
                using var scope = _scopeFactory.CreateScope();
                var enrichUseCase = scope.ServiceProvider.GetRequiredService<IBuildEnrichedFinancialDataEventUseCase>();
                var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

                try
                {
                    var ingested = messageEnvelope.Data;
                    var idempotencyKey = DataIngestedEventIdempotencyKeys.Build(ingested);
                    if (await idempotency.HasProcessedAsync(idempotencyKey, stoppingToken))
                    {
                        _logger.LogInformation(
                            "[{CorrelationId}] Skipping already processed document {DocumentId} (key={Key})",
                            correlationId,
                            ingested.DocumentId,
                            idempotencyKey);
                        await _eventConsumer.AcknowledgeMessageAsync(messageEnvelope, stoppingToken);
                        continue;
                    }

                    var raw = DataIngestedToRawFinancialMapper.ToRawFinancial(ingested);
                    var enrichedEvent = await enrichUseCase.ExecuteAsync(raw, stoppingToken);

                    _logger.LogInformation(
                        "[{CorrelationId}] Enriched document {DocumentId}: Category={Category}, Supplier={Supplier}",
                        correlationId,
                        ingested.DocumentId,
                        enrichedEvent.Category,
                        enrichedEvent.Supplier.NormalizedName);

                    await idempotency.MarkProcessedAsync(idempotencyKey, _idempotencyTtl, stoppingToken);
                    await _eventConsumer.AcknowledgeMessageAsync(messageEnvelope, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[{CorrelationId}] Failed to enrich DataIngestedEvent document {DocumentId}",
                        correlationId,
                        messageEnvelope.Data.DocumentId);
                    await _eventConsumer.RejectMessageAsync(messageEnvelope, stoppingToken);
                }
            }
        }
    }
}
