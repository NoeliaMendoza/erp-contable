
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Purchases.Models;
using ERP.Tributario.Identification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Modules.Purchases.Data;

/// <summary>
/// Contexto de persistencia del módulo Purchases.
/// Utiliza el esquema purchases de PostgreSQL.
/// </summary>
public sealed class PurchasesDbContext(
    DbContextOptions<PurchasesDbContext> options,
    ITenantContext tenantContext)
    : TenantAwareDbContext(options, tenantContext)
{
    public const string Schema = "purchases";

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Supplier>(b =>
        {
            var supplierTypes = string.Join(", ", EcuadorIdentificationCatalog.ForSuppliers
                .Select(type => $"'{type.SriCode}'"));
            b.ToTable("suppliers", table =>
            {
                table.HasCheckConstraint("ck_suppliers_identification_type", $"identification_type IN ({supplierTypes})");
                table.HasCheckConstraint("ck_suppliers_identification_number_nonempty", "length(btrim(identification_number)) > 0");
                table.HasCheckConstraint("ck_suppliers_name_nonempty", "length(btrim(name)) > 0");
            });

            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });

            b.HasIndex(x => new
            {
                x.TenantId,
                x.IdentificationType,
                x.IdentificationNumber
            }).IsUnique().HasDatabaseName("ux_suppliers_tenant_identification");

            b.Property(x => x.IdentificationType)
                .HasMaxLength(Supplier.IdentificationTypeMaxLength)
                .IsRequired();

            b.Property(x => x.IdentificationNumber)
                .HasColumnType("text")
                .IsRequired();

            b.Property(x => x.Name)
                .HasMaxLength(Supplier.NameMaxLength)
                .IsRequired();

            b.Property(x => x.Email)
                .HasMaxLength(Supplier.EmailMaxLength);

            b.Property(x => x.Phone)
                .HasMaxLength(Supplier.PhoneMaxLength);

            b.Property(x => x.Address)
                .HasMaxLength(Supplier.AddressMaxLength);
        });

        modelBuilder.Entity<Purchase>(b =>
        {
            b.ToTable("purchases", table =>
            {
                table.HasCheckConstraint("ck_purchases_total_cost_nonnegative", "total_cost >= 0");
                table.HasCheckConstraint("ck_purchases_number_nonempty", "length(btrim(number)) > 0");
            });
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique()
                .HasDatabaseName("ux_purchases_tenant_number");
            b.Property(x => x.Number).HasMaxLength(30).IsRequired();
            b.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(x => x.TotalCost).HasPrecision(18, 2);
            b.Ignore(x => x.ReceiptIdempotencyKey);
            b.HasOne<Supplier>().WithMany().HasForeignKey(x => new { x.TenantId, x.SupplierId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseLine>(b =>
        {
            b.ToTable("purchase_lines", table =>
            {
                table.HasCheckConstraint("ck_purchase_lines_quantity_positive", "quantity > 0");
                table.HasCheckConstraint("ck_purchase_lines_unit_cost_nonnegative", "unit_cost >= 0");
                table.HasCheckConstraint("ck_purchase_lines_line_cost_nonnegative", "line_cost >= 0");
            });
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.PurchaseId, x.ProductId }).IsUnique()
                .HasDatabaseName("ux_purchase_lines_purchase_product");
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.Quantity).HasPrecision(18, 4);
            b.Property(x => x.UnitCost).HasPrecision(18, 4);
            b.Property(x => x.LineCost).HasPrecision(18, 2);
            b.HasOne<Purchase>().WithMany(x => x.Lines).HasForeignKey(x => new { x.TenantId, x.PurchaseId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Factory utilizada por dotnet ef para generar migraciones.
/// </summary>
internal sealed class PurchasesDesignTimeFactory
    : IDesignTimeDbContextFactory<PurchasesDbContext>
{
    public PurchasesDbContext CreateDbContext(string[] args)
    {
        var builder =
            new DbContextOptionsBuilder<PurchasesDbContext>();

        PersistenceRegistration.ConfigureNpgsql(
            builder,
            "Host=localhost;Database=erp_design_time",
            PurchasesDbContext.Schema);

        return new PurchasesDbContext(
            builder.Options,
            TenantSession.ForSystem());
    }
}
