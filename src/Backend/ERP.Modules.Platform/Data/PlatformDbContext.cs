using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Platform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Modules.Platform.Data;

/// <summary>
/// DbContext privado del módulo Platform (esquema <c>platform</c>). Otros módulos no deben usarlo:
/// consumen tenant/identidad mediante <see cref="ITenantContext"/> y contratos.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenantContext)
    : TenantAwareDbContext(options, tenantContext)
{
    public const string Schema = "platform";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();

    public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Taxpayer> Taxpayers => Set<Taxpayer>();

    public DbSet<Establishment> Establishments => Set<Establishment>();

    public DbSet<EmissionPoint> EmissionPoints => Set<EmissionPoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(x => x.Email).HasMaxLength(320).IsRequired();
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<TenantMembership>(b =>
        {
            b.ToTable("tenant_memberships");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            b.HasIndex(x => x.UserId);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions");
            b.HasKey(x => new { x.RoleId, x.PermissionCode });
            b.Property(x => x.PermissionCode).HasMaxLength(100);
            b.HasIndex(x => new { x.TenantId, x.RoleId });
            b.HasOne<Role>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(r => new { r.TenantId, r.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MembershipRole>(b =>
        {
            b.ToTable("membership_roles");
            b.HasKey(x => new { x.MembershipId, x.RoleId });
            b.HasIndex(x => new { x.TenantId, x.MembershipId });
            b.HasIndex(x => new { x.TenantId, x.RoleId });
            b.HasOne<TenantMembership>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MembershipId })
                .HasPrincipalKey(m => new { m.TenantId, m.Id })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Role>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(r => new { r.TenantId, r.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantFeature>(b =>
        {
            b.ToTable("tenant_features");
            b.HasKey(x => new { x.TenantId, x.FeatureCode });
            b.Property(x => x.FeatureCode).HasMaxLength(50);
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.City).HasMaxLength(100);
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Taxpayer>(b =>
        {
            b.ToTable("taxpayers");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Ruc }).IsUnique();
            b.Property(x => x.Ruc).HasMaxLength(13).IsFixedLength().IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(300).IsRequired();
            b.Property(x => x.TradeName).HasMaxLength(300);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.SpecialTaxpayerResolution).HasMaxLength(50);
            b.Property(x => x.TaxRegimeCode).HasMaxLength(50);
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Establishment>(b =>
        {
            b.ToTable("establishments");
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.TaxpayerId, x.Code }).IsUnique();
            b.Property(x => x.Code).HasMaxLength(3).IsFixedLength().IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Address).HasMaxLength(300);
            b.HasOne<Taxpayer>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.TaxpayerId })
                .HasPrincipalKey(t => new { t.TenantId, t.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.BranchId })
                .HasPrincipalKey(br => new { br.TenantId, br.Id })
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmissionPoint>(b =>
        {
            b.ToTable("emission_points");
            b.HasIndex(x => new { x.TenantId, x.EstablishmentId, x.Code }).IsUnique();
            b.Property(x => x.Code).HasMaxLength(3).IsFixedLength().IsRequired();
            b.Property(x => x.Description).HasMaxLength(200).IsRequired();
            b.HasOne<Establishment>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.EstablishmentId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Filtros de tenant y demás convenciones comunes (después de configurar las entidades).
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Usado solo por <c>dotnet ef</c> para generar migraciones (no se conecta a ninguna base).</summary>
internal sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceRegistration.ConfigureNpgsql(builder, "Host=localhost;Database=erp_design_time", PlatformDbContext.Schema);
        return new PlatformDbContext(builder.Options, TenantSession.ForSystem());
    }
}
