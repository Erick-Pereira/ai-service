namespace Simcag.AIService.Api.Controllers.Financial;

/// <summary>Corpo de <c>POST …/categorize|classify</c>.</summary>
public sealed record ClassifyRequest(string RawText, string? DocumentType, string? Source);
