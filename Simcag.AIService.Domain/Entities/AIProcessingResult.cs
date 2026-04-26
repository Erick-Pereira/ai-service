using Simcag.AIService.Domain.ValueObjects;
using Simcag.Shared.Common;

namespace Simcag.AIService.Domain.Entities;

/// <summary>Resultado do processamento de um texto por IA.</summary>
public class AIProcessingResult : BaseEntity
{
    public string InputText { get; private set; } = string.Empty;
    public string ProcessedText { get; private set; } = string.Empty;
    public string OperationType { get; private set; } = string.Empty;
    public ConfidenceScore Confidence { get; private set; }
    public string AIModel { get; private set; } = string.Empty;
    public DateTime ProcessedAt { get; private set; }
    public bool UsedFallback { get; private set; }
    public string ResultJson { get; private set; } = string.Empty;
    public string RawResponse { get; private set; } = string.Empty;

    private AIProcessingResult() { }

    private AIProcessingResult(string inputText, string processedText, string operationType, ConfidenceScore confidence, string aiModel, string resultJson, string rawResponse, bool usedFallback)
    {
        InputText = inputText;
        ProcessedText = processedText;
        OperationType = operationType;
        Confidence = confidence;
        AIModel = aiModel;
        ResultJson = resultJson;
        RawResponse = rawResponse;
        UsedFallback = usedFallback;
        ProcessedAt = DateTime.UtcNow;
    }

    public static AIProcessingResult Create(string inputText, string processedText, string operationType, ConfidenceScore confidence, string aiModel, string resultJson, string rawResponse, bool usedFallback)
    {
        if (string.IsNullOrWhiteSpace(inputText)) throw new ArgumentException("Input text is required", nameof(inputText));
        if (string.IsNullOrWhiteSpace(operationType)) throw new ArgumentException("Operation type is required", nameof(operationType));

        return new AIProcessingResult(inputText, processedText, operationType, confidence, aiModel, resultJson, rawResponse, usedFallback);
    }
}
