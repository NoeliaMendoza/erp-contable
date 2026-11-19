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
    public DbSet<JournalEntryEntity> JournalEntries => Set<JournalEntryEntity>();
    public DbSet<JournalEntryLineEntity> JournalLines => Set<JournalEntryLineEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<JournalEntryEntity>(b =>
        {
            b.ToTable("journal_entries");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        });
        modelBuilder.Entity<JournalEntryLineEntity>(b =>
        {
            b.ToTable("journal_entry_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountCode).HasMaxLength(30).IsRequired();
            b.Property(x => x.Debit).HasPrecision(18, 2);
            b.Property(x => x.Credit).HasPrecision(18, 2);
            b.HasOne<JournalEntryEntity>().WithMany().HasForeignKey(x => new { x.TenantId, x.JournalEntryId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class AccountingDesignTimeFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AccountingDbContext>();
        PersistenceRegistration.ConfigureNpgsql(builder, "Host=localhost;Database=erp_design_time", AccountingDbContext.Schema);
        return new AccountingDbContext(builder.Options, TenantSession.ForSystem());
    }
}
