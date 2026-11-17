using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Modules.Inventory.Data;

/// <summary>DbContext privado del módulo Inventario (esquema <c>inventory</c>).</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenantContext)
    : TenantAwareDbContext(options, tenantContext)
{
    public const string Schema = "inventory";

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Unit>(b =>
        {
            b.ToTable("units");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.Property(x => x.Code).HasMaxLength(Unit.CodeMaxLength).IsRequired();
            b.Property(x => x.Name).HasMaxLength(Unit.NameMaxLength).IsRequired();
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.UnitId });
            b.Property(x => x.Sku).HasMaxLength(Product.SkuMaxLength).IsRequired();
            b.Property(x => x.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Price).HasPrecision(18, Product.PriceScale);

            // FK compuesta: la unidad debe ser de la MISMA empresa (la base lo impide aunque falle el código).
            b.HasOne<Unit>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.UnitId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Usado solo por <c>dotnet ef</c> para generar migraciones (no se conecta a ninguna base).</summary>
internal sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<InventoryDbContext>();
        PersistenceRegistration.ConfigureNpgsql(builder, "Host=localhost;Database=erp_design_time", InventoryDbContext.Schema);
        return new InventoryDbContext(builder.Options, TenantSession.ForSystem());
    }
}
