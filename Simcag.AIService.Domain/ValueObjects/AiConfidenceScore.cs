namespace Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Value object para pontuação de confiança de IA (0 a 1).
/// </summary>
public readonly record struct ConfidenceScore
{
    public decimal Value { get; init; }

    public ConfidenceScore(decimal value)
    {
        if (value < 0m || value > 1m)
            throw new ArgumentOutOfRangeException(nameof(value), "Confidence must be between 0 and 1");
        Value = value;
    }

    public static implicit operator decimal(ConfidenceScore s) => s.Value;
    public static explicit operator ConfidenceScore(decimal v) => new(v);
    public override string ToString() => Value.ToString("P2");
}
