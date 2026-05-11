using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Services;
using Simcag.AIService.Application.UseCases.Financial;
using Simcag.AIService.Domain.Services;
using Simcag.AIService.Infrastructure.Cache;
using Simcag.AIService.Infrastructure.Clients;
using Simcag.AIService.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Simcag.AIService.Api.Extensions;

/// <summary>
/// Extensions for registering AI Service dependencies following Clean Architecture.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIServices(this IServiceCollection services)
    {
        // Cache: Redis optional. If REDIS_CONNECTION is empty, use in-memory distributed cache.
        // (No appsettings: env only.)
        var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Cross-cutting stores (idempotency + normalization cache)
        services.AddSingleton<IIdempotencyStore, DistributedIdempotencyStore>();
        services.AddSingleton<INormalizationCache, DistributedNormalizationCache>();
        services.AddSingleton<IAiInferenceCache, DistributedAiInferenceCache>();

        // Infrastructure - HTTP Client (Ollama)
        services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
        {
            // Geração LLM pode exceder 30s; default 180s (sobreponha com OLLAMA_TIMEOUT_SECONDS).
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("OLLAMA_TIMEOUT_SECONDS"), out var s)
                ? s
                : 180;
            if (seconds < 30)
                seconds = 30;
            client.Timeout = TimeSpan.FromSeconds(seconds);
        });

        // Infrastructure - Repositories (in-memory for dev)
        services.AddSingleton<ICategoryRepository, InMemoryCategoryRepository>();
        services.AddSingleton<IAIProcessingResultRepository, InMemoryAIProcessingResultRepository>();

        // Domain Services (stateless)
        services.AddSingleton<ICategoryMatcher, CategoryMatcher>();
        services.AddSingleton<ICategoryResponseExtractor, CategoryResponseExtractor>();
        services.AddSingleton<IConfidenceCalculator, ConfidenceCalculator>();

        // Application Services - Financial Domain
        services.AddScoped<IExpenseClassificationService, ExpenseClassificationService>();
        services.AddScoped<ISupplierExtractionService, SupplierExtractionService>();
        services.AddScoped<IFinancialLineItemsExtractionService, FinancialLineItemsExtractionService>();
        services.AddScoped<INameNormalizationService, NameNormalizationService>();
        services.AddScoped<IFinancialEnrichmentOrchestrator, FinancialEnrichmentOrchestrator>();

        // Financial use cases (HTTP + workers depend on these abstractions; implementations delegate to services above)
        services.AddScoped<IClassifyExpenseUseCase, ClassifyExpenseUseCase>();
        services.AddScoped<IExtractSupplierUseCase, ExtractSupplierUseCase>();
        services.AddScoped<INormalizeSupplierNameUseCase, NormalizeSupplierNameUseCase>();
        services.AddScoped<IBuildEnrichedFinancialDataEventUseCase, BuildEnrichedFinancialDataEventUseCase>();
        services.AddScoped<IPreviewFinancialEnrichmentUseCase, PreviewFinancialEnrichmentUseCase>();

        return services;
    }
}
