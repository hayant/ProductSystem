using ProductSystem.Shared.Domain;

namespace ProductSystem.Api.Contracts;

// DTOs kept separate from the domain entity — never leak domain internals over HTTP.
public record ProductDto(Guid Id, string Sku, string Name, decimal Price, DateTime UpdatedAt);

public record ProductRequest(string Sku, string Name, decimal Price);

public static class ProductMappings
{
    public static ProductDto ToDto(this Product p)
        => new(p.Id, p.Sku, p.Name, p.Price, p.UpdatedAt);
}
