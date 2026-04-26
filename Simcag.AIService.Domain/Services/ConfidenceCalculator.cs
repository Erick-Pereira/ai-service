namespace Simcag.AIService.Domain.Services;

using Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Calcula o score de confiança com base na resposta do modelo de IA.
/// </summary>
public interface IConfidenceCalculator
{
    ConfidenceScore Calculate(string aiResponse, CategoryName category);
}

/// <summary>
/// Implementação simples: baseia-se no comprimento da resposta e na presença clara da categoria.
/// Valores retornados entre 0.5-0.9 para respostas válidas, 0.4 se incerto.
/// </summary>
public sealed class ConfidenceCalculator : IConfidenceCalculator
{
    public ConfidenceScore Calculate(string aiResponse, CategoryName category)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
            return new ConfidenceScore(0.1m);

        var trimmed = aiResponse.Trim();

        // Se a resposta é curta e contém a categoria → alta confiança
        if (trimmed.Length <= 50 && trimmed.Contains(category.Value, StringComparison.OrdinalIgnoreCase))
            return new ConfidenceScore(0.9m);

        // Se contém a categoria → confiança média-alta
        if (trimmed.Contains(category.Value, StringComparison.OrdinalIgnoreCase))
            return new ConfidenceScore(0.7m);

        // Se não contém a categoria → baixa confiança
        return new ConfidenceScore(0.4m);
    }
}
