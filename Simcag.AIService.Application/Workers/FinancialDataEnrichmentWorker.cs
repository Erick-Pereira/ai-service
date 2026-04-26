using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.UseCases.Financial;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Workers;

/// <summary>
/// Consumidor de eventos RabbitMQ que processa dados financeiros brutos através do pipeline de IA.
/// Consome RawFinancialDataEvent → enriquece → publica EnrichedFinancialDataEvent.
/// </summary>
public sealed class FinancialDataEnrichmentWorker : BackgroundService
{
    private readonly IEventConsumer<RawFinancialDataEvent> _eventConsumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinancialDataEnrichmentWorker> _logger;
    private readonly TimeSpan _idempotencyTtl;

    public FinancialDataEnrichmentWorker(
        IEventConsumer<RawFinancialDataEvent> eventConsumer,
        IServiceScopeFactory scopeFactory,
        ILogger<FinancialDataEnrichmentWorker> logger)
    {
        _eventConsumer = eventConsumer;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _idempotencyTtl = AiServiceEnvironment.IdempotencyTtl;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting FinancialDataEnrichmentWorker - listening for RawFinancialDataEvent");

        await foreach (var messageEnvelope in _eventConsumer.ReadMessagesAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var enrichUseCase = scope.ServiceProvider.GetRequiredService<IBuildEnrichedFinancialDataEventUseCase>();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

            try
            {
                var idempotencyKey = RawFinancialEventIdempotencyKeys.Build(messageEnvelope.Data);
                if (await idempotency.HasProcessedAsync(idempotencyKey, stoppingToken))
                {
                    _logger.LogInformation(
                        "Skipping already processed RawFinancialDataEvent for document {DocumentId} (key={Key})",
                        messageEnvelope.Data.DocumentId, idempotencyKey);
                    await _eventConsumer.AcknowledgeMessageAsync(messageEnvelope, stoppingToken);
                    continue;
                }

                var enrichedEvent = await enrichUseCase.ExecuteAsync(messageEnvelope.Data, stoppingToken);

                _logger.LogInformation(
                    "Enriched financial data for document {DocumentId}: Category={Category}, Supplier={Supplier}",
                    enrichedEvent.DocumentId, enrichedEvent.Category, enrichedEvent.Supplier.NormalizedName);

                await idempotency.MarkProcessedAsync(idempotencyKey, _idempotencyTtl, stoppingToken);

                // RabbitMQ consumer uses manual ACK. Without this, messages will be re-delivered indefinitely.
                await _eventConsumer.AcknowledgeMessageAsync(messageEnvelope, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich financial data for document {DocumentId}",
                    messageEnvelope.Data.DocumentId);
                await _eventConsumer.RejectMessageAsync(messageEnvelope, stoppingToken);
            }
        }

        _logger.LogInformation("FinancialDataEnrichmentWorker stopped");
    }

}
