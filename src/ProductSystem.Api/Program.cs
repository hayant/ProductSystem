using Microsoft.EntityFrameworkCore;
using ProductSystem.Api.Middleware;
using ProductSystem.Shared.Data;
using ProductSystem.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from config — appsettings, User Secrets (locally),
// or env vars (App Service sets ConnectionStrings__ProductSystem). We fail loudly
// at startup if it's missing rather than discovering it on the first request.
var connectionString = builder.Configuration.GetConnectionString("ProductSystem")
    ?? throw new InvalidOperationException("ConnectionStrings:ProductSystem is not configured.");

builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ProductService>();

// Health checks expose a /health endpoint App Service can probe. The DbContext
// check pings the database, so the endpoint goes Unhealthy if the DB is down.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ProductDbContext>("database");

// CORS origins are environment-specific — localhost in dev, the production
// frontend URL in App Service. Read from config so the binary doesn't change.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Apply pending migrations on startup. Fine for a smoke-test environment;
// a production setup would run migrations as a separate CI step to avoid
// race conditions when multiple instances start at once.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    db.Database.Migrate();
}

// UseCors must come first so that browser preflight (OPTIONS) requests
// are answered before the API key middleware runs — otherwise the browser
// never sees the CORS headers and blocks the preflight.
app.UseCors();

// Health endpoint is intentionally unauthenticated so App Service can probe it.
// The ApiKeyMiddleware also short-circuits on /health for defence in depth.
app.MapHealthChecks("/health");

app.UseMiddleware<ApiKeyMiddleware>();

// Versioned API prefix — baked in from day one, as discussed in the design.
var api = app.MapGroup("/api/v1");

api.MapGet("/products", async (ProductService service, CancellationToken ct) =>
{
    var products = await service.ListAsync(ct);
    return Results.Ok(products.Select(ToDto));
});

api.MapPost("/products", async (CreateProductRequest request, ProductService service, CancellationToken ct) =>
{
    try
    {
        var product = await service.CreateAsync(request.Sku, request.Name, request.Price, ct);
        return Results.Created($"/api/v1/products/{product.Id}", ToDto(product));
    }
    catch (ArgumentException ex)
    {
        // Validation failure -> 400 with a structured body the frontend can render.
        return Results.BadRequest(new { error = "validation_failed", message = ex.Message });
    }
    catch (DuplicateSkuException ex)
    {
        // Uniqueness conflict -> 409. Distinct from 400 because the client needs to react differently.
        return Results.Conflict(new { error = "duplicate_sku", message = ex.Message, sku = ex.Sku });
    }
});

app.Run();

// DTOs kept separate from the domain entity — never leak domain internals over HTTP.
static ProductDto ToDto(ProductSystem.Shared.Domain.Product p)
    => new(p.Id, p.Sku, p.Name, p.Price, p.UpdatedAt);

record ProductDto(Guid Id, string Sku, string Name, decimal Price, DateTime UpdatedAt);
record CreateProductRequest(string Sku, string Name, decimal Price);
