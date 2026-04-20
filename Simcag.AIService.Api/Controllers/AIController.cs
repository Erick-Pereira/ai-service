using Microsoft.AspNetCore.Mvc;
using Simcag.AIService.Application.Interfaces;
using Simcag.Shared.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.AIService.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;

    public AIController(IAIService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("categorize")]
    public async Task<IActionResult> Categorize([FromBody] CategorizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductDescription))
        {
            return BadRequest(ApiResponse<string>.Fail("Product description is required"));
        }

        var result = await _aiService.CategorizeProductAsync(request.ProductDescription, ct);

        return Ok(ApiResponse<CategoryResult>.Ok(result));
    }

    [HttpPost("extract")]
    public async Task<IActionResult> ExtractEntities([FromBody] ExtractRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductDescription))
        {
            return BadRequest(ApiResponse<string>.Fail("Product description is required"));
        }

        var result = await _aiService.ExtractEntitiesAsync(request.ProductDescription, ct);

        return Ok(ApiResponse<EntityExtractionResult>.Ok(result));
    }

    [HttpPost("standardize")]
    public async Task<IActionResult> StandardizeName([FromBody] StandardizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return BadRequest(ApiResponse<string>.Fail("Product name is required"));
        }

        var result = await _aiService.StandardizeNameAsync(request.ProductName, ct);

        return Ok(ApiResponse<StandardizationResult>.Ok(result));
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessProduct([FromBody] ProcessProductRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductDescription))
        {
            return BadRequest(ApiResponse<string>.Fail("Product description is required"));
        }

        // Process all AI operations in parallel for better performance
        var categorizationTask = _aiService.CategorizeProductAsync(request.ProductDescription, ct);
        var extractionTask = _aiService.ExtractEntitiesAsync(request.ProductDescription, ct);
        var standardizationTask = _aiService.StandardizeNameAsync(request.ProductDescription, ct);

        await Task.WhenAll(categorizationTask, extractionTask, standardizationTask);

        var result = new ProductProcessingResult
        {
            OriginalDescription = request.ProductDescription,
            Category = await categorizationTask,
            Entities = await extractionTask,
            StandardizedName = await standardizationTask,
            ProcessedAt = DateTime.UtcNow
        };

        return Ok(ApiResponse<ProductProcessingResult>.Ok(result));
    }
}

// Request/Response DTOs
public class CategorizeRequest
{
    public string ProductDescription { get; set; } = string.Empty;
}

public class ExtractRequest
{
    public string ProductDescription { get; set; } = string.Empty;
}

public class StandardizeRequest
{
    public string ProductName { get; set; } = string.Empty;
}

public class ProcessProductRequest
{
    public string ProductDescription { get; set; } = string.Empty;
}

public class ProductProcessingResult
{
    public string OriginalDescription { get; set; } = string.Empty;
    public CategoryResult Category { get; set; } = new();
    public EntityExtractionResult Entities { get; set; } = new();
    public StandardizationResult StandardizedName { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
}