namespace Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Nome da categoria de um produto.
/// Deve ser um dos valores pré-definidos ou "Outro".
/// </summary>
public readonly record struct CategoryName
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Notebook", "Monitor", "Periférico", "Hardware", "Software", "Outro"
    };

    public string Value { get; init; }

    public CategoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Category name is required", nameof(value));

        if (!AllowedCategories.Contains(value))
            throw new ArgumentException($"Invalid category. Must be one of: {string.Join(", ", AllowedCategories)}", nameof(value));

        Value = value;
    }

    public static implicit operator string(CategoryName category) => category.Value;
    public static explicit operator CategoryName(string value) => new(value);

    public override string ToString() => Value;
}
