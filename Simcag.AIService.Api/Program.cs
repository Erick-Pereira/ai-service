using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Services;
using Simcag.AIService.Application.Workers;
using Simcag.AIService.Api;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Contracts;
using Simcag.Shared.Messaging.Extensions;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HTTP Client for Ollama
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Mock repositories for now (will be replaced with real implementations)
builder.Services.AddSingleton<ICategoryRepository, MockCategoryRepository>();
builder.Services.AddSingleton<IAIProcessingResultRepository, MockAIProcessingResultRepository>();

// AI Service
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddRabbitMqEventPublisher<NormalizedDataEvent>("simcag-events");
builder.Services.AddRabbitMqEventPublisher<CategorizedDataEvent>("simcag-events");

// RabbitMQ Configuration
var rabbitMqOptions = new RabbitMqOptions
{
    Host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost",
    Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ__PORT") ?? "5672"),
    UserName = Environment.GetEnvironmentVariable("RABBITMQ__USERNAME") ?? "admin",
    Password = Environment.GetEnvironmentVariable("RABBITMQ__PASSWORD") ?? "admin",
    VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ__VIRTUALHOST") ?? "/"
};

builder.Services.AddRabbitMqMessaging(rabbitMqOptions);
builder.Services.AddRabbitMqEventConsumer<PriceCollectedEvent>("raw-data-events");
builder.Services.AddRabbitMqEventPublisher<NormalizedDataEvent>("simcag-events");
builder.Services.AddRabbitMqEventPublisher<CategorizedDataEvent>("simcag-events");

// Background Services
builder.Services.AddHostedService<RawDataEventConsumer>();

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();