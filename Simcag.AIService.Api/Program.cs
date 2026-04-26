using Microsoft.AspNetCore.OpenApi;
using Simcag.AIService.Api.Extensions;
using Simcag.AIService.Api.OpenApi;
using Simcag.AIService.Application.Workers;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info ??= new();
        document.Info.Title = "SIMCAG AI Service";
        document.Info.Version = "v1";
        document.Info.Description =
            "Camada cognitiva: enriquecimento financeiro (auditoria condominial). Rotas /api/ai espelham o pipeline assíncrono (raw-financial-data → enriched).";
        return Task.CompletedTask;
    });
    options.AddOperationTransformer<FinancialAuditOpenApiOperationTransformer>();
});

// Registro centralizado dos serviços AI (Financial domain)
builder.Services.AddAIServices();

static string? GetEnv(params string[] keys)
{
    foreach (var key in keys)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }
    return null;
}

// RabbitMQ
var rabbitOptions = new RabbitMqOptions
{
    Host = GetEnv("RABBITMQ_HOST", "RABBITMQ__HOST") ?? "localhost",
    Port = int.Parse(GetEnv("RABBITMQ_PORT", "RABBITMQ__PORT") ?? "5672"),
    UserName = GetEnv("RABBITMQ_USERNAME", "RABBITMQ__USERNAME") ?? "admin",
    Password = GetEnv("RABBITMQ_PASSWORD", "RABBITMQ__PASSWORD") ?? "admin",
    VirtualHost = GetEnv("RABBITMQ_VIRTUALHOST", "RABBITMQ__VIRTUALHOST") ?? "/"
};

builder.Services.AddRabbitMqMessaging(rabbitOptions);

// Exchange direct de eventos de domínio (não usar EventBusConstants.ExchangeName = price-monitoring-exchange).
// Fallback literal: compatível com pacotes Simcag.Shared antigos sem DefaultEventsExchange / GetEventsExchangeName.
const string defaultDomainEventsExchange = "events";
var eventsExchangeFromEnv = GetEnv("RABBITMQ_EVENTS_EXCHANGE", "RABBITMQ__EVENTS_EXCHANGE");
var eventsExchange = string.IsNullOrWhiteSpace(eventsExchangeFromEnv)
    ? defaultDomainEventsExchange
    : eventsExchangeFromEnv.Trim();

// Consumers
builder.Services.AddRabbitMqEventConsumer<RawFinancialDataEvent>(EventNames.RawFinancialData, eventsExchange);

// Publishers
builder.Services.AddRabbitMqEventPublisher<EnrichedFinancialDataEvent>(eventsExchange);

// Worker
builder.Services.AddHostedService<FinancialDataEnrichmentWorker>();

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Em Development, clientes HTTP (ex.: curl na porta http) não devem levar 307 -> https (POST vira confuso / 405 no fallback).
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();
