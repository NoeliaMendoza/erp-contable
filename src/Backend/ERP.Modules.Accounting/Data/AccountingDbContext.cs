using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Accounting.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Modules.Accounting.Data;

public sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options, ITenantContext tenantContext)
    : TenantAwareDbContext(options, tenantContext)
{
    public const string Schema = "accounting";

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("accounts", t =>
            {
                t.HasCheckConstraint("ck_accounts_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                t.HasCheckConstraint("ck_accounts_kind", "kind IN ('Asset', 'Liability', 'Equity', 'Income', 'Expense')");
                t.HasCheckConstraint("ck_accounts_normal_balance", "normal_balance IN ('Debit', 'Credit')");
                t.HasCheckConstraint("ck_accounts_code", "code ~ '^[A-Z0-9._-]{1,40}$'");
                t.HasCheckConstraint("ck_accounts_name", "length(btrim(name)) BETWEEN 1 AND 160");
            });
            b.HasAlternateKey(a => new { a.TenantId, a.Id });
            b.HasIndex(a => new { a.TenantId, a.Code }).IsUnique();
            b.Property(a => a.Code).HasMaxLength(Account.CodeMaxLength).IsRequired();
            b.Property(a => a.Name).HasMaxLength(Account.NameMaxLength).IsRequired();
            b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.NormalBalance).HasConversion<string>().HasMaxLength(10);
            b.HasOne<Account>().WithMany()
                .HasForeignKey(a => new { a.TenantId, a.ParentId })
                .HasPrincipalKey(a => new { a.TenantId, a.Id })
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Solo construcción del modelo; no abre conexiones ni genera migraciones automáticamente.</summary>
internal sealed class AccountingDesignTimeFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, "Host=localhost;Database=erp_design_time", AccountingDbContext.Schema);
        return new AccountingDbContext(options.Options, TenantSession.ForSystem());
    }
}
