namespace ProductSystem.Shared.Domain;

// The core domain entity. Kept deliberately small for the smoke test,
// but note two production-minded choices already baked in:
//   - A public Id (Guid) separate from any DB-generated key.
//   - A RowVersion for optimistic concurrency (the EF pattern you're already familiar with).
//   - An ExternalId for the ERP's view of this product, nullable so locally-created products
//     can exist before they've been synced outbound.
public class Product
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public decimal Price { get; private set; }
    public string? ExternalId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; private set; } = [];

    // EF needs a parameterless constructor; the factory method is what application code uses.
    private Product() { }
    
    public static Product Create(string sku, string name, decimal price)
    {
        ValidateSku(sku);
        ValidateName(name);
        ValidatePrice(price);

        return new Product
        {
            Sku = sku.Trim(),
            Name = name.Trim(),
            Price = price,
        };
    }

    public void UpdateDetails(string name, decimal price)
    {
        ValidateName(name);
        ValidatePrice(price);
        Name = name.Trim();
        Price = price;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required.", nameof(sku));
        if (sku.Length > 50)
            throw new ArgumentException("SKU must be 50 characters or fewer.", nameof(sku));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Length > 200)
            throw new ArgumentException("Name must be 200 characters or fewer.", nameof(name));
    }

    private static void ValidatePrice(decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));
    }
}
