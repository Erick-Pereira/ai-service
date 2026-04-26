namespace Simcag.AIService.Domain.Services;

using Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Serviço de domínio para classificar um produto em uma categoria com base em regras (fallback).
/// </summary>
public interface ICategoryMatcher
{
    CategoryName MatchCategory(string productDescription);
}

/// <summary>
/// Implementação concreta do CategoryMatcher com keyword mapping.
/// </summary>
public sealed class CategoryMatcher : ICategoryMatcher
{
    private static readonly Dictionary<string, string> KeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Notebooks
        { "notebook", "Notebook" },
        { "laptop", "Notebook" },
        { "macbook", "Notebook" },
        { "ultrabook", "Notebook" },
        // Monitores
        { "monitor", "Monitor" },
        { "display", "Monitor" },
        { "screen", "Monitor" },
        // Periféricos
        { "mouse", "Periférico" },
        { "teclado", "Periférico" },
        { "keyboard", "Periférico" },
        { "headset", "Periférico" },
        { "webcam", "Periférico" },
        { "web cam", "Periférico" },
        { "câmera", "Periférico" },
        { "camera", "Periférico" },
        // Hardware
        { "cpu", "Hardware" },
        { "processador", "Hardware" },
        { "processor", "Hardware" },
        { "gpu", "Hardware" },
        { "placa de vídeo", "Hardware" },
        { "video card", "Hardware" },
        { "memória", "Hardware" },
        { "memory", "Hardware" },
        { "ram", "Hardware" },
        { "ssd", "Hardware" },
        { "hdd", "Hardware" },
        { "disco", "Hardware" },
        { "hard disk", "Hardware" },
        { "placa-mãe", "Hardware" },
        { "motherboard", "Hardware" },
        // Software
        { "software", "Software" },
        { "licença", "Software" },
        { "license", "Software" },
        { "assinatura", "Software" },
        { "subscription", "Software" },
        { "windows", "Software" },
        { "office", "Software" },
        { "antivírus", "Software" },
        { "antivirus", "Software" }
    };

    public CategoryName MatchCategory(string productDescription)
    {
        foreach (var (keyword, category) in KeywordMap)
        {
            if (productDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return new CategoryName(category);
        }

        return new CategoryName("Outro");
    }
}
