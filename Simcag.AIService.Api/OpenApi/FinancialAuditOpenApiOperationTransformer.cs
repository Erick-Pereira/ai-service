using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Simcag.AIService.Api.OpenApi;

/// <summary>
/// Agrupa operações do controller financeiro no documento OpenAPI gerado por <c>MapOpenApi</c>.
/// </summary>
internal sealed class FinancialAuditOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is ControllerActionDescriptor cad)
        {
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

        return Task.CompletedTask;
    }
}
