using ProductSystem.Api.Contracts;
using ProductSystem.Shared.Services;

namespace ProductSystem.Api.Endpoints;

// Product endpoints, mapped onto the versioned API group from Program.cs.
// Handlers stay on the happy path — validation and uniqueness failures bubble up
// as exceptions and are translated centrally by ProductExceptionHandler.
public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/products", async (ProductService service, CancellationToken ct) =>
        {
            var products = await service.ListAsync(ct);
            return Results.Ok(products.Select(p => p.ToDto()));
        });

        group.MapPost("/products", async (ProductRequest request, ProductService service, CancellationToken ct) =>
        {
            var product = await service.CreateAsync(request.Sku, request.Name, request.Price, ct);
            return Results.Created($"/api/v1/products/{product.Id}", product.ToDto());
        });

        return group;
    }
}
