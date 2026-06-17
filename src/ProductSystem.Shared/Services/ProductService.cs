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

    // Offset paging keeps response size and memory bounded — the caller (API) clamps `take`
    // so a client can't ask for the whole table in one go.
    public Task<List<Product>> ListAsync(int skip, int take, CancellationToken ct = default)
        => _db.Products.AsNoTracking().OrderBy(p => p.Sku).Skip(skip).Take(take).ToListAsync(ct);

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

    // The batch write path for the ERP sync. Same invariants as CreateAsync (validation in the
    // entity, SKU uniqueness), but for N products it does ONE SKU pre-check query + ONE
    // SaveChanges instead of N×2 sequential round-trips. Per-item outcomes are returned so the
    // caller keeps today's idempotent behaviour: an already-existing SKU is a Duplicate (skip),
    // not a failure, and one bad record never sinks the rest of the batch.
    public async Task<BatchCreateResult> CreateManyAsync(IReadOnlyList<CreateProductInput> items, CancellationToken ct = default)
    {
        var results = new List<BatchItemResult>(items.Count);
        var toInsert = new List<Product>();
        // SKUs claimed within this batch (validated/trimmed) — guards against duplicates that
        // both arrive in the same request and so wouldn't be caught by the DB pre-check.
        var seenInBatch = new HashSet<string>();

        foreach (var item in items)
        {
            Product product;
            try
            {
                // Validation lives in the entity — single source of truth, same as CreateAsync.
                product = Product.Create(item.Sku, item.Name, item.Price);
            }
            catch (ArgumentException ex)
            {
                results.Add(new BatchItemResult(item.Sku, CreateItemOutcome.Invalid, ex.Message));
                continue;
            }

            if (!seenInBatch.Add(product.Sku))
            {
                results.Add(new BatchItemResult(product.Sku, CreateItemOutcome.Duplicate, "Duplicate SKU within the batch."));
                continue;
            }

            toInsert.Add(product);
            // Placeholder; flipped to Duplicate below if the SKU already exists in the DB.
            results.Add(new BatchItemResult(product.Sku, CreateItemOutcome.Created, null));
        }

        if (toInsert.Count > 0)
        {
            // One round-trip to find which of the candidate SKUs already exist — EF emits a
            // single `WHERE Sku IN (...)`, replacing the per-record AnyAsync pre-check.
            var candidateSkus = toInsert.Select(p => p.Sku).ToList();
            var existing = await _db.Products
                .Where(p => candidateSkus.Contains(p.Sku))
                .Select(p => p.Sku)
                .ToListAsync(ct);
            var existingSet = existing.ToHashSet();

            if (existingSet.Count > 0)
            {
                toInsert.RemoveAll(p => existingSet.Contains(p.Sku));
                for (var i = 0; i < results.Count; i++)
                {
                    if (results[i].Outcome == CreateItemOutcome.Created && existingSet.Contains(results[i].Sku))
                        results[i] = results[i] with { Outcome = CreateItemOutcome.Duplicate, Message = "SKU already exists." };
                }
            }

            if (toInsert.Count > 0)
            {
                _db.Products.AddRange(toInsert);
                // One SaveChanges for the whole batch. The unique index is the last line of
                // defence: were a concurrent writer to insert a colliding SKU between the
                // pre-check and here, this throws DbUpdateException and the whole batch fails.
                // The sync worker runs single-instance/run-once, so that race is not a practical
                // concern — we let it propagate (API -> 500 -> worker counts the batch failed and
                // the next run retries) rather than add machinery for a case that can't occur here.
                await _db.SaveChangesAsync(ct);
            }
        }

        var created = results.Count(r => r.Outcome == CreateItemOutcome.Created);
        var duplicate = results.Count(r => r.Outcome == CreateItemOutcome.Duplicate);
        var invalid = results.Count(r => r.Outcome == CreateItemOutcome.Invalid);

        _logger.LogInformation(
            "Batch create complete. Created: {Created}, Duplicate: {Duplicate}, Invalid: {Invalid}",
            created, duplicate, invalid);

        return new BatchCreateResult(created, duplicate, invalid, results);
    }
}

// Domain-shaped batch contract — no HTTP concerns leak in here; the API maps these to/from DTOs.
public record CreateProductInput(string Sku, string Name, decimal Price);

public enum CreateItemOutcome { Created, Duplicate, Invalid }

public record BatchItemResult(string Sku, CreateItemOutcome Outcome, string? Message);

public record BatchCreateResult(int Created, int Duplicate, int Invalid, IReadOnlyList<BatchItemResult> Items);

public class DuplicateSkuException : Exception
{
    public string Sku { get; }
    public DuplicateSkuException(string sku) : base($"Product with SKU '{sku}' already exists.")
        => Sku = sku;
}
