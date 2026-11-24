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

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementLineEntity> InventoryMovementLines => Set<InventoryMovementLineEntity>();

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

        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Name).HasMaxLength(Category.NameMaxLength).IsRequired();

            // Unicidad sin distinguir mayúsculas: índice único sobre upper(name) creado con SQL en la migración.
        });

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.ToTable("warehouses");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            // Una sola bodega principal por empresa.
            b.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_default").HasDatabaseName("ux_warehouses_tenant_id_default");
            b.Property(x => x.Code).HasMaxLength(Warehouse.CodeMaxLength).IsRequired();
            b.Property(x => x.Name).HasMaxLength(Warehouse.NameMaxLength).IsRequired();
        });

        modelBuilder.Entity<StockBalance>(b =>
        {
            b.ToTable("stock_balances");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.WarehouseId }).IsUnique();
            b.Property(x => x.QuantityOnHand).HasPrecision(18, StockBalance.QuantityScale);
            b.Property(x => x.QuantityReserved).HasPrecision(18, StockBalance.QuantityScale);
            b.Property(x => x.AverageCost).HasPrecision(18, StockBalance.CostScale);
            b.Ignore(x => x.Available);

            // Artículo y bodega de la MISMA empresa.
            b.HasOne<Product>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProductId })
                .HasPrincipalKey(p => new { p.TenantId, p.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.TenantId, x.WarehouseId })
                .HasPrincipalKey(w => new { w.TenantId, w.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryMovement>(b =>
        {
            b.ToTable("inventory_movements");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.SourceModule).HasMaxLength(30).IsRequired();
            b.Property(x => x.SourceDocumentType).HasMaxLength(30).IsRequired();
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.TotalCost).HasPrecision(18, StockBalance.AmountScale);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.TenantId, x.WarehouseId })
                .HasPrincipalKey(w => new { w.TenantId, w.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryMovementLineEntity>(b =>
        {
            b.ToTable("inventory_movement_lines");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.MovementId, x.LineNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.Sequence });
            b.Property(x => x.Quantity).HasPrecision(18, StockBalance.QuantityScale);
            b.Property(x => x.UnitCost).HasPrecision(18, StockBalance.CostScale);
            b.Property(x => x.LineCost).HasPrecision(18, StockBalance.AmountScale);
            b.Property(x => x.QuantityAfter).HasPrecision(18, StockBalance.QuantityScale);
            b.Property(x => x.AverageCostAfter).HasPrecision(18, StockBalance.CostScale);
            b.HasOne<InventoryMovement>().WithMany(m => m.Lines).HasForeignKey(x => new { x.TenantId, x.MovementId })
                .HasPrincipalKey(m => new { m.TenantId, m.Id }).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Product>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProductId })
                .HasPrincipalKey(p => new { p.TenantId, p.Id }).OnDelete(DeleteBehavior.Restrict);
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
            b.Property(x => x.Cost).HasPrecision(18, Product.PriceScale);
            b.Property(x => x.MinStock).HasPrecision(18, Product.QuantityScale);
            b.Property(x => x.VatTreatment).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Barcode).HasMaxLength(Product.BarcodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(Product.DescriptionMaxLength);
            b.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.CategoryId });

            // FK compuesta: la unidad debe ser de la MISMA empresa (la base lo impide aunque falle el código).
            b.HasOne<Unit>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.UnitId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id })
                .OnDelete(DeleteBehavior.Restrict);

            // Categoría opcional, también de la MISMA empresa (con category_id NULL la FK no se evalúa).
            b.HasOne<Category>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.CategoryId })
                .HasPrincipalKey(c => new { c.TenantId, c.Id })
                .IsRequired(false)
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
