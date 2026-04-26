namespace Simcag.AIService.Domain.Services;

using Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Serviço responsável por extrair o nome da categoria a partir da resposta do modelo de IA.
/// </summary>
public interface ICategoryResponseExtractor
{
    CategoryName Extract(string aiResponse);
}

/// <summary>
/// Implementação que busca por categorias conhecidas no texto da resposta.
/// </summary>
public sealed class CategoryResponseExtractor : ICategoryResponseExtractor
{
    private static readonly string[] KnownCategories = { "Notebook", "Monitor", "Periférico", "Hardware", "Software", "Outro" };

    public CategoryName Extract(string aiResponse)
    {
        foreach (var category in KnownCategories)
        {
            if (aiResponse.Contains(category, StringComparison.OrdinalIgnoreCase))
                return new CategoryName(category);
        }

        return new CategoryName("Outro");
    }
}
