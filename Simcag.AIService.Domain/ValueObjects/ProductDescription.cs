namespace Simcag.AIService.Domain.ValueObjects;

public readonly record struct ProductDescription
{
    public string Value { get; init; }

    public ProductDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Product description cannot be empty", nameof(value));
        Value = value;
    }

    public static implicit operator string(ProductDescription description) => description.Value;
    public static implicit operator ProductDescription(string value) => new(value);
}
