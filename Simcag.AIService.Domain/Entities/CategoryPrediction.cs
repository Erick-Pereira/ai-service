namespace Simcag.AIService.Domain.Entities;

/// <summary>Predição de categoria (resultado de modelos de ML).</summary>
public class CategoryPrediction
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
