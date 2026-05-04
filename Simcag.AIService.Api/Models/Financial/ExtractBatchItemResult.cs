using Simcag.AIService.Application.Contracts;

namespace Simcag.AIService.Api.Models.Financial;

public sealed record ExtractBatchItemResult(string? Id, bool Success, string? Error, SupplierExtractionResult? Supplier);
