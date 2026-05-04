using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Simcag.AIService.Api.OpenApi;

/// <summary>
/// Agrupa operações dos controllers financeiros no documento Swagger.
/// </summary>
internal sealed class FinancialAiSwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor cad)
            return;

        if (string.Equals(cad.ControllerName, "FinancialAI", StringComparison.OrdinalIgnoreCase))
        {
            operation.Tags ??= new HashSet<OpenApiTagReference>();
            operation.Tags.Add(new OpenApiTagReference("Financial audit (condominial)"));
        }
        else if (string.Equals(cad.ControllerName, "FinancialAiDiagnostics", StringComparison.OrdinalIgnoreCase))
        {
            operation.Tags ??= new HashSet<OpenApiTagReference>();
            operation.Tags.Add(new OpenApiTagReference("AI Service — system & diagnostics"));
        }
    }
}
