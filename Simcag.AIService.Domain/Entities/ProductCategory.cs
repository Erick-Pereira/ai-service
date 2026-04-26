using Simcag.AIService.Domain.ValueObjects;
using Simcag.Shared.Common;

namespace Simcag.AIService.Domain.Entities;

/// <summary>Entidade de categoria de produto.</summary>
public class ProductCategory : BaseEntity
{
    public CategoryName Name { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid? ParentCategoryId { get; private set; }
    public ProductCategory? ParentCategory { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public ICollection<ProductCategory> SubCategories { get; private set; } = new List<ProductCategory>();

    private ProductCategory() { }

    public static ProductCategory Create(string name, string description, Guid? parentCategoryId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = new CategoryName(name),
            Description = description,
            ParentCategoryId = parentCategoryId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    public void Update(string name, string description)
    {
        Name = new CategoryName(name);
        Description = description;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
