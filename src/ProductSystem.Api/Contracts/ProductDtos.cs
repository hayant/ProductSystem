using ProductSystem.Shared.Domain;
using ProductSystem.Shared.Services;

namespace ProductSystem.Api.Contracts;

// DTOs kept separate from the domain entity — never leak domain internals over HTTP.
public record ProductDto(Guid Id, string Sku, string Name, decimal Price, DateTime UpdatedAt);

public record ProductRequest(string Sku, string Name, decimal Price);

// Batch create response — per-item outcomes plus aggregate counts.
public record BatchItemResultDto(string Sku, string Outcome, string? Message);

public record BatchCreateResponse(int Created, int Duplicate, int Invalid, BatchItemResultDto[] Items);

public static class ProductMappings
{
    public static ProductDto ToDto(this Product p)
        => new(p.Id, p.Sku, p.Name, p.Price, p.UpdatedAt);

    public static BatchCreateResponse ToResponse(this BatchCreateResult result)
        => new(
            result.Created,
            result.Duplicate,
            result.Invalid,
            result.Items.Select(i => new BatchItemResultDto(i.Sku, i.Outcome.ToString(), i.Message)).ToArray());
}
