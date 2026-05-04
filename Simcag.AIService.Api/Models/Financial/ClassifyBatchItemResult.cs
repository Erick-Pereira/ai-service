using Simcag.AIService.Application.Contracts;

namespace Simcag.AIService.Api.Models.Financial;

public sealed record ClassifyBatchItemResult(string? Id, bool Success, string? Error, CategoryResult? Category);
