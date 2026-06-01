using Simcag.AIService.Api.Extensions;
using Simcag.AIService.Api.OpenApi;
using Simcag.AIService.Application.Workers;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using Simcag.Shared.ErrorHandling;
using Simcag.Shared.Hosting;
using Simcag.Shared.Security;
using Simcag.Shared.Telemetry;

DotNetEnv.Env.NoClobber().Load();
ContainerListenConfiguration.NormalizeAspNetCoreListenUrlsInContainer();

var builder = WebApplication.CreateBuilder(args);
builder.AddSimcagDistributedTelemetry("Simcag.AIService");
ContainerListenConfiguration.ApplyDockerListenUrls(builder);

var isTesting = builder.Environment.IsEnvironment("Testing");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "SIMCAG AI Service",
        Version = "v1",
        Description =
            "Camada cognitiva: enriquecimento financeiro (auditoria condominial). Rotas /api/ai espelham o pipeline assíncrono (raw-financial-data → enriched)."
    });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Cole apenas o JWT (sem 'Bearer ')."
    });
    c.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
    c.OperationFilter<FinancialAiSwaggerOperationFilter>();
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

// RabbitMQ (omitido em Testing para WebApplicationFactory)
if (!isTesting)
{
    var rabbitOptions = new RabbitMqOptions
    {
        Host = GetEnv("RABBITMQ__HOST", "RABBITMQ_HOST") ?? "localhost",
        Port = int.Parse(GetEnv("RABBITMQ__PORT", "RABBITMQ_PORT") ?? "5672"),
        UserName = GetEnv("RABBITMQ__USERNAME", "RABBITMQ_USERNAME") ?? "guest",
        Password = GetEnv("RABBITMQ__PASSWORD", "RABBITMQ_PASSWORD") ?? "guest",
        VirtualHost = GetEnv("RABBITMQ__VIRTUALHOST", "RABBITMQ_VIRTUALHOST") ?? "/"
    };
    rabbitOptions.ApplyMessageSigningFromEnvironment();

    builder.Services.AddRabbitMqMessaging(rabbitOptions);

    const string defaultDomainEventsExchange = "events";
    var eventsExchangeFromEnv = GetEnv("RABBITMQ__EVENTS_EXCHANGE", "RABBITMQ_EVENTS_EXCHANGE");
    var eventsExchange = string.IsNullOrWhiteSpace(eventsExchangeFromEnv)
        ? defaultDomainEventsExchange
        : eventsExchangeFromEnv.Trim();

    builder.Services.AddRabbitMqEventConsumer<RawFinancialDataEvent>(EventNames.RawFinancialData, eventsExchange);
    builder.Services.AddRabbitMqEventPublisher<EnrichedFinancialDataEvent>(eventsExchange);
    builder.Services.AddHostedService<FinancialDataEnrichmentWorker>();
}

builder.Services.AddSimcagGatewayAuthentication(builder.Environment);

builder.Services.AddHealthChecks().AddSimcagLiveSelfCheck();

builder.Services.AddSimcagProblemDetails();

var app = builder.Build();

app.ValidateSimcagGatewayTrustAtStartup();

app.UseSimcagExceptionHandler();
app.UseSimcagHttpCorrelationActivityTags();

// Única UI: Swagger (sem fallback HTML em wwwroot).
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SIMCAG AI Service v1");
    c.RoutePrefix = "swagger";
});

// Em Development, clientes HTTP (ex.: curl na porta http) não devem levar 307 -> https (POST vira confuso / 405 no fallback).
if (!isTesting)
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapSimcagHealthChecks();

app.UseSimcagTelemetryEndpoints();

app.Run();

public partial class Program
{
}
