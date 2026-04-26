namespace Simcag.AIService.Api.Controllers.Financial;

/// <summary>Corpo de <c>POST …/extract</c>.</summary>
public sealed record ExtractSupplierRequest(string RawText, string? DocumentType, string? Source);
