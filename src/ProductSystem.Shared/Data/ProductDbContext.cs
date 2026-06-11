using Microsoft.EntityFrameworkCore;
using ProductSystem.Shared.Domain;

namespace ProductSystem.Shared.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();

        product.HasKey(p => p.Id);
        product.Property(p => p.Sku).IsRequired().HasMaxLength(50);
        product.HasIndex(p => p.Sku).IsUnique();
        product.Property(p => p.Name).IsRequired().HasMaxLength(200);
        product.Property(p => p.Price).HasPrecision(18, 2);
        product.Property(p => p.ExternalId).HasMaxLength(100);

        // The RowVersion pattern — EF will check this on UPDATE and throw
        // DbUpdateConcurrencyException if another transaction changed the row first.
        // The in-memory provider doesn't actually enforce this, but the shape is correct
        // for when you swap to SQL Server.
        product.Property(p => p.RowVersion).IsRowVersion();
    }
}
