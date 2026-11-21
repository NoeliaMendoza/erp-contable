using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Sales.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Modules.Sales.Data;

public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ITenantContext tenantContext)
    : TenantAwareDbContext(options, tenantContext)
{
    public const string Schema = "sales";
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Sale>(b =>
        {
            b.ToTable("sales");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            b.Property(x => x.Number).HasMaxLength(30).IsRequired();
            b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Subtotal).HasPrecision(18, 2);
            b.Property(x => x.TaxTotal).HasPrecision(18, 2);
            b.Property(x => x.Total).HasPrecision(18, 2);
        });
        modelBuilder.Entity<SaleLine>(b =>
        {
            b.ToTable("sale_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sku).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Quantity).HasPrecision(18, 4);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.Discount).HasPrecision(18, 2);
            b.Property(x => x.TaxRate).HasPrecision(8, 4);
            b.Property(x => x.TaxAmount).HasPrecision(18, 2);
            b.Property(x => x.LineTotal).HasPrecision(18, 2);
            b.HasOne<Sale>().WithMany(s => s.Lines).HasForeignKey(x => new { x.TenantId, x.SaleId })
                .HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("customers");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.IdentificationType, x.IdentificationNumber }).IsUnique();
            b.Property(x => x.IdentificationType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.IdentificationNumber).HasMaxLength(Customer.IdentificationNumberMaxLength).IsRequired();
            b.Property(x => x.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
            b.Property(x => x.Email).HasMaxLength(Customer.EmailMaxLength);
            b.Property(x => x.Phone).HasMaxLength(Customer.PhoneMaxLength);
            b.Property(x => x.Address).HasMaxLength(Customer.AddressMaxLength);
        });
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class SalesDesignTimeFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SalesDbContext>();
        PersistenceRegistration.ConfigureNpgsql(builder, "Host=localhost;Database=erp_design_time", SalesDbContext.Schema);
        return new SalesDbContext(builder.Options, TenantSession.ForSystem());
    }
}
