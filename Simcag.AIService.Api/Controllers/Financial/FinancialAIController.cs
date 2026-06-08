using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simcag.AIService.Api.Mapping;
using Simcag.AIService.Api.Models.Financial;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Exceptions;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.UseCases.Financial;
using Simcag.Shared.Contracts;
using Simcag.Shared.Events;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simcag.AIService.Api.Controllers.Financial;

/// <summary>
/// API síncrona do pipeline financeiro. Rotas canónicas: <c>categorize</c>, <c>extract</c>, <c>standardize</c>, <c>process</c>
/// (aliases <c>classify</c>, <c>normalize</c>, <c>enrich</c> mantidos).
/// <c>POST …/process</c> e <c>POST …/process/from-raw</c> publicam <see cref="EnrichedFinancialDataEvent"/> no exchange configurado;
/// <c>POST …/enrich</c>, <c>…/enrich/preview</c> e <c>…/enrich/from-raw</c> executam o mesmo pipeline sem publicar. Batches nunca publicam.
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class FinancialAIController : ControllerBase
{
    private readonly IClassifyExpenseUseCase _classifyExpense;
    private readonly IExtractSupplierUseCase _extractSupplier;
    private readonly INormalizeSupplierNameUseCase _normalizeSupplierName;
    private readonly IBuildEnrichedFinancialDataEventUseCase _buildEnrichedEvent;
    private readonly IPreviewFinancialEnrichmentUseCase _previewEnrichment;
    private readonly ILogger<FinancialAIController> _logger;

    public FinancialAIController(
        IClassifyExpenseUseCase classifyExpense,
        IExtractSupplierUseCase extractSupplier,
        INormalizeSupplierNameUseCase normalizeSupplierName,
        IBuildEnrichedFinancialDataEventUseCase buildEnrichedEvent,
        IPreviewFinancialEnrichmentUseCase previewEnrichment,
        ILogger<FinancialAIController> logger)
    {
        _classifyExpense = classifyExpense;
        _extractSupplier = extractSupplier;
        _normalizeSupplierName = normalizeSupplierName;
        _buildEnrichedEvent = buildEnrichedEvent;
        _previewEnrichment = previewEnrichment;
        _logger = logger;
    }

    /// <summary>Logging helper para erros de AI service</summary>
    private static ObjectResult AiFailure(AiServiceException ex) =>
        new(ApiResponse<string>.Fail(ex.Message)) { StatusCode = StatusCodes.Status503ServiceUnavailable };

    /// <summary>Logs exception com contexto estruturado</summary>
    private IActionResult LogException(Exception ex, string operationId, string endpoint)
    {
        var errorContext = new
        {
            Endpoint = endpoint,
            OperationId = operationId,
            ExceptionType = ex.GetType().FullName,
            ErrorMessage = ex.Message,
            Timestamp = DateTime.UtcNow.ToString("o"),
            StackTrace = ex.StackTrace
        };

        _logger.LogError(
            JsonExceptionSerializer.Serialize(errorContext),
            "Error in {Endpoint}: {ErrorMessage}",
            endpoint,
            ex.Message);

        return Problem(title: "Internal Server Error", detail: ex.Message, statusCode: 500);
    }

    /// <summary>Logs info com contexto estruturado</summary>
    private void LogInfo(string operationId, string endpoint, string message)
    {
        _logger.LogInformation(
            JsonExceptionSerializer.Serialize(new { Endpoint = endpoint, OperationId = operationId, Message = message }),
            message);
    }

    private async Task<ClassifyBatchItemResult> ProcessClassifyBatchItemAsync(
        string id, string rawText, IClassifyExpenseUseCase classifyExpense, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ClassifyBatchItemResult(id, false, "Raw text is required", null);

        try
        {
            var financialEvent = FinancialRawEventFactory.FromClassifyOrExtract(rawText, null, null);
            var result = await classifyExpense.ExecuteAsync(financialEvent, ct);
            return new ClassifyBatchItemResult(id, true, null, result);
        }
        catch (AiServiceException ex)
        {
            return new ClassifyBatchItemResult(id, false, ex.Message, null);
        }
    }

    private async Task<ExtractBatchItemResult> ProcessExtractBatchItemAsync(
        string id, string rawText, IExtractSupplierUseCase extractSupplier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ExtractBatchItemResult(id, false, "Raw text is required", null);

        try
        {
            var financialEvent = FinancialRawEventFactory.FromClassifyOrExtract(rawText, null, null);
            var result = await extractSupplier.ExecuteAsync(financialEvent, ct);
            return new ExtractBatchItemResult(id, true, null, result);
        }
        catch (AiServiceException ex)
        {
            return new ExtractBatchItemResult(id, false, ex.Message, null);
        }
    }

    private async Task<NormalizeBatchItemResult> ProcessNormalizeBatchItemAsync(
        string id, string rawName, INormalizeSupplierNameUseCase normalizeSupplierName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return new NormalizeBatchItemResult(id, false, "Raw name is required", null);

        try
        {
            var result = await normalizeSupplierName.ExecuteAsync(rawName, ct);
            return new NormalizeBatchItemResult(id, true, null, result);
        }
        catch (AiServiceException ex)
        {
            return new NormalizeBatchItemResult(id, false, ex.Message, null);
        }
    }

    [HttpPost("categorize")]
    [HttpPost("classify")]
    public async Task<IActionResult> ClassifyExpense([FromBody] ClassifyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
            return BadRequest(ApiResponse<string>.Fail("Raw text is required"));

        try
        {
            var financialEvent = FinancialRawEventFactory.FromClassifyOrExtract(request.RawText, request.DocumentType, request.Source);
            var result = await _classifyExpense.ExecuteAsync(financialEvent, ct);
            return Ok(ApiResponse<CategoryResult>.Ok(result));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    [HttpPost("classify/batch")]
    public async Task<IActionResult> ClassifyBatch([FromBody] ClassifyBatchRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(ApiResponse<string>.Fail("Items is required and must not be empty"));
        if (request.Items.Count > FinancialRawEventFactory.MaxBatchItems)
            return BadRequest(ApiResponse<string>.Fail($"At most {FinancialRawEventFactory.MaxBatchItems} items per request"));

        var results = new List<ClassifyBatchItemResult>(request.Items.Count);
        foreach (var item in request.Items)
        {
            results.Add(await ProcessClassifyBatchItemAsync(
                item.Id, item.RawText, _classifyExpense, ct));
        }

        return Ok(ApiResponse<IReadOnlyList<ClassifyBatchItemResult>>.Ok(results));
    }

    /// <summary>Extração a partir do texto: fornecedor (nome, documento) + produto/serviço.</summary>
    [HttpPost("extract")]
    [HttpPost("extract-supplier")]
    public async Task<IActionResult> ExtractSupplier([FromBody] ExtractSupplierRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
            return BadRequest(ApiResponse<string>.Fail("Raw text is required"));

        try
        {
            var financialEvent = FinancialRawEventFactory.FromClassifyOrExtract(request.RawText, request.DocumentType, request.Source);
            var result = await _extractSupplier.ExecuteAsync(financialEvent, ct);
            return Ok(ApiResponse<SupplierExtractionResult>.Ok(result));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    [HttpPost("extract/batch")]
    public async Task<IActionResult> ExtractBatch([FromBody] ExtractBatchRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(ApiResponse<string>.Fail("Items is required and must not be empty"));
        if (request.Items.Count > FinancialRawEventFactory.MaxBatchItems)
            return BadRequest(ApiResponse<string>.Fail($"At most {FinancialRawEventFactory.MaxBatchItems} items per request"));

        var results = new List<ExtractBatchItemResult>(request.Items.Count);
        foreach (var item in request.Items)
        {
            results.Add(await ProcessExtractBatchItemAsync(
                item.Id, item.RawText, _extractSupplier, ct));
        }

        return Ok(ApiResponse<IReadOnlyList<ExtractBatchItemResult>>.Ok(results));
    }

    [HttpPost("standardize")]
    [HttpPost("normalize")]
    public async Task<IActionResult> Normalize([FromBody] NormalizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawName))
            return BadRequest(ApiResponse<string>.Fail("Raw name is required"));

        try
        {
            var result = await _normalizeSupplierName.ExecuteAsync(request.RawName, ct);
            return Ok(ApiResponse<NormalizedNameResult>.Ok(result));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    [HttpPost("normalize/batch")]
    public async Task<IActionResult> NormalizeBatch([FromBody] NormalizeBatchRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(ApiResponse<string>.Fail("Items is required and must not be empty"));
        if (request.Items.Count > FinancialRawEventFactory.MaxBatchItems)
            return BadRequest(ApiResponse<string>.Fail($"At most {FinancialRawEventFactory.MaxBatchItems} items per request"));

        var results = new List<NormalizeBatchItemResult>(request.Items.Count);
        foreach (var item in request.Items)
        {
            results.Add(await ProcessNormalizeBatchItemAsync(
                item.Id, item.RawName, _normalizeSupplierName, ct));
        }

        return Ok(ApiResponse<IReadOnlyList<NormalizeBatchItemResult>>.Ok(results));
    }

    [HttpPost("process")]
    public async Task<IActionResult> Process([FromBody] EnrichRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
            return BadRequest(ApiResponse<string>.Fail("Raw text is required"));

        try
        {
            var financialEvent = FinancialRawEventFactory.FromEnrichRequest(request);
            var enriched = await _buildEnrichedEvent.ExecuteAsync(financialEvent, ct);
            return Ok(ApiResponse<EnrichedFinancialDataEvent>.Ok(enriched));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    /// <summary>Pipeline completo sem publicar no RabbitMQ (UI de revisão / testes). <c>enrich/preview</c> é alias desta rota.</summary>
    [HttpPost("enrich")]
    [HttpPost("enrich/preview")]
    public async Task<IActionResult> EnrichPreview([FromBody] EnrichRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
            return BadRequest(ApiResponse<string>.Fail("Raw text is required"));

        try
        {
            var financialEvent = FinancialRawEventFactory.FromEnrichRequest(request);
            var enriched = await _previewEnrichment.ExecuteAsync(financialEvent, ct);
            return Ok(ApiResponse<EnrichedFinancialDataEvent>.Ok(enriched));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    /// <summary>Publica no Rabbit após enriquecer um <see cref="RawFinancialDataEvent"/> completo (reprocessamento / backfill).</summary>
    [HttpPost("process/from-raw")]
    public async Task<IActionResult> ProcessFromRaw([FromBody] RawFinancialDataEvent payload, CancellationToken ct)
    {
        if (payload is null)
            return BadRequest(ApiResponse<string>.Fail("Body is required"));
        if (string.IsNullOrWhiteSpace(payload.RawText))
            return BadRequest(ApiResponse<string>.Fail("RawText is required"));

        try
        {
            var normalized = FinancialRawEventFactory.NormalizeFromApiPayload(payload);
            var enriched = await _buildEnrichedEvent.ExecuteAsync(normalized, ct);
            return Ok(ApiResponse<EnrichedFinancialDataEvent>.Ok(enriched));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    /// <summary>Aceita o mesmo contrato do evento Rabbit <see cref="RawFinancialDataEvent"/>, sem publicar (pré-visualização).</summary>
    [HttpPost("enrich/from-raw")]
    public async Task<IActionResult> EnrichFromRaw([FromBody] RawFinancialDataEvent payload, CancellationToken ct)
    {
        if (payload is null)
            return BadRequest(ApiResponse<string>.Fail("Body is required"));
        if (string.IsNullOrWhiteSpace(payload.RawText))
            return BadRequest(ApiResponse<string>.Fail("RawText is required"));

        try
        {
            var normalized = FinancialRawEventFactory.NormalizeFromApiPayload(payload);
            var enriched = await _previewEnrichment.ExecuteAsync(normalized, ct);
            return Ok(ApiResponse<EnrichedFinancialDataEvent>.Ok(enriched));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

    /// <summary>Pré-visualização a partir de um <see cref="RawFinancialDataEvent"/> completo, sem publicar.</summary>
    [HttpPost("enrich/from-raw/preview")]
    public async Task<IActionResult> EnrichFromRawPreview([FromBody] RawFinancialDataEvent payload, CancellationToken ct)
    {
        if (payload is null)
            return BadRequest(ApiResponse<string>.Fail("Body is required"));
        if (string.IsNullOrWhiteSpace(payload.RawText))
            return BadRequest(ApiResponse<string>.Fail("RawText is required"));

        try
        {
            var normalized = FinancialRawEventFactory.NormalizeFromApiPayload(payload);
            var enriched = await _previewEnrichment.ExecuteAsync(normalized, ct);
            return Ok(ApiResponse<EnrichedFinancialDataEvent>.Ok(enriched));
        }
        catch (AiServiceException ex)
        {
            return AiFailure(ex);
        }
    }

}

public static class JsonExceptionSerializer
{
    public static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
}
