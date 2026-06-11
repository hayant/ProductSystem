using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductSystem.Shared.Domain;

namespace ProductSystem.Shared.Services;

// This is the key architectural piece. Both the API (user-driven) and the Worker (ERP-driven)
// go through this service. That means validation, SKU uniqueness, and all other invariants
// live in exactly one place rather than being duplicated across two entry points.
public class ProductService
{
    private readonly Data.ProductDbContext _db;
    private readonly ILogger<ProductService> _logger;

    public ProductService(Data.ProductDbContext db, ILogger<ProductService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<List<Product>> ListAsync(CancellationToken ct = default)
        => _db.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync(ct);

    public async Task<Product> CreateAsync(string sku, string name, decimal price, CancellationToken ct = default)
    {
        // Uniqueness check at the app layer gives a clean error; the unique index in the
        // DB is the last-line-of-defence (belt and braces).
        var exists = await _db.Products.AnyAsync(p => p.Sku == sku, ct);
        if (exists)
            throw new DuplicateSkuException(sku);

        var product = Product.Create(sku, name, price);
        
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created product {Sku} ({Id})", product.Sku, product.Id);
        return product;
    }
}

public class DuplicateSkuException : Exception
{
    public string Sku { get; }
    public DuplicateSkuException(string sku) : base($"Product with SKU '{sku}' already exists.")
        => Sku = sku;
}
