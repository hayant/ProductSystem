using ProductSystem.Api.Contracts;
using ProductSystem.Shared.Services;

namespace ProductSystem.Api.Endpoints;

// Product endpoints, mapped onto the versioned API group from Program.cs.
// Handlers stay on the happy path — validation and uniqueness failures bubble up
// as exceptions and are translated centrally by ProductExceptionHandler.
public static class ProductEndpoints
{
    // Paging bounds for GET /products.
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/products", async (int? skip, int? take, ProductService service, CancellationToken ct) =>
        {
            // Offset paging. No params => first page; `take` is clamped so a client can't pull the
            // whole table in one request, and `skip` can't go negative. Still returns a plain
            // array, so existing callers (the frontend) are unaffected.
            var pageSkip = Math.Max(0, skip ?? 0);
            var pageTake = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
            var products = await service.ListAsync(pageSkip, pageTake, ct);
            return Results.Ok(products.Select(p => p.ToDto()));
        });

        group.MapPost("/products", async (ProductRequest request, ProductService service, CancellationToken ct) =>
        {
            var product = await service.CreateAsync(request.Sku, request.Name, request.Price, ct);
            return Results.Created($"/api/v1/products/{product.Id}", product.ToDto());
        });

        // Batch create — the scalable write path for the ERP sync. Per-item outcomes are reported
        // in the body (created/duplicate/invalid), so a duplicate SKU is an idempotent skip and one
        // bad record doesn't fail the whole request. A malformed request (empty/missing array)
        // bubbles up as an ArgumentException -> 400, via the central ProductExceptionHandler.
        group.MapPost("/products/batch", async (ProductRequest[]? requests, ProductService service, CancellationToken ct) =>
        {
            if (requests is null || requests.Length == 0)
                throw new ArgumentException("Request body must be a non-empty array of products.");

            var inputs = requests.Select(r => new CreateProductInput(r.Sku, r.Name, r.Price)).ToList();
            var result = await service.CreateManyAsync(inputs, ct);
            return Results.Ok(result.ToResponse());
        });

        return group;
    }
}
