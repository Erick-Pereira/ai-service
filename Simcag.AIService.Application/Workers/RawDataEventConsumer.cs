using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Domain.Entities;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Application.Workers;

public class RawDataEventConsumer : BackgroundService
{
    private readonly IEventConsumer<PriceCollectedEvent> _eventConsumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RawDataEventConsumer> _logger;

    public RawDataEventConsumer(
        IEventConsumer<PriceCollectedEvent> eventConsumer,
        IServiceScopeFactory scopeFactory,
        ILogger<RawDataEventConsumer> logger)
    {
        _eventConsumer = eventConsumer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RawDataEvent consumer for AI processing");

        await foreach (var messageEnvelope in _eventConsumer.ReadMessagesAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var aiService = scope.ServiceProvider.GetRequiredService<IAIService>();

            try
            {
                await aiService.ProcessRawDataAsync(messageEnvelope.Data, stoppingToken);
                await _eventConsumer.AcknowledgeMessageAsync(messageEnvelope, stoppingToken);
                _logger.LogInformation("Successfully processed RawDataEvent for product {ProductId}",
                    messageEnvelope.Data.ProductId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process RawDataEvent for product {ProductId}",
                    messageEnvelope.Data.ProductId);
                await _eventConsumer.RejectMessageAsync(messageEnvelope, stoppingToken);
            }
        }

        _logger.LogInformation("RawDataEvent consumer stopped");
    }
}