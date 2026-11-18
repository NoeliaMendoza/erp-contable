using ERP.Infrastructure.Persistence;
using ERP.Modules.Platform.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenIddict.EntityFrameworkCore.Models;

namespace ERP.Modules.Platform.Data;

/// <summary>
/// DbContext de autenticación (esquema <c>identity</c>): credenciales de ASP.NET Core Identity y almacén de OpenIddict
/// (clientes, autorizaciones y tokens). Es un contexto aparte porque Identity exige heredar de
/// <see cref="IdentityUserContext{TUser,TKey}"/> y porque estas tablas son <b>globales</b> (sin <c>tenant_id</c> ni RLS):
/// la identidad de una persona es la misma en todas sus empresas. Solo lo usa la infraestructura de inicio de sesión.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityUserContext<ErpIdentityUser, Guid>(options)
{
    public const string Schema = "identity";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ErpIdentityUser>(b =>
        {
            b.ToTable("users");
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.NormalizedEmail).HasMaxLength(320);
            b.Property(x => x.UserName).HasMaxLength(320);
            b.Property(x => x.NormalizedUserName).HasMaxLength(320);
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.UseOpenIddict<Guid>();
        builder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("oidc_applications");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("oidc_authorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("oidc_scopes");
        builder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("oidc_tokens");
    }

    /// <summary>Configura Npgsql con las convenciones del repositorio (snake_case, historial en el esquema propio).</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        PersistenceRegistration.ConfigureNpgsql(options, connectionString, Schema);
        options.UseOpenIddict<Guid>();
    }
}

/// <summary>Usado solo por <c>dotnet ef</c> para generar migraciones (no se conecta a ninguna base).</summary>
internal sealed class AuthDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(builder, "Host=localhost;Database=erp_design_time");
        return new AuthDbContext(builder.Options);
    }
}
