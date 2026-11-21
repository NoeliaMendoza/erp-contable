
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
