using ERP.BuildingBlocks.Domain;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Web;
using ERP.Modules.Accounting;
using ERP.Modules.Accounting.Controllers;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Accounting.Models;
using ERP.Modules.Accounting.Validators;
using Microsoft.EntityFrameworkCore;

namespace ERP.UnitTests.Accounting;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static AccountData Data(string code = "01.01", Guid? parent = null) =>
        new(code, "Caja", AccountKind.Asset, NormalBalance.Debit, parent, true);

    [Theory]
    [InlineData(" 01.ab ", "01.AB")]
    [InlineData("001.01", "001.01")]
    public void Codigo_es_texto_normalizado_y_conserva_ceros(string input, string expected)
    {
        var account = Account.Create(Guid.NewGuid(), Guid.NewGuid(), Data(input), Now, null);

        Assert.Equal(expected, account.Code);
        Assert.True(account.IsActive);
        Assert.True(account.AllowsPosting);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacio")]
    [InlineData("01/%")]
    public void Codigo_invalido_se_rechaza(string code) =>
        Assert.Throws<DomainException>(() => Account.Create(Guid.NewGuid(), Guid.NewGuid(), Data(code), Now, null));

    [Fact]
    public void Cuenta_no_puede_ser_su_propio_padre()
    {
        var id = Guid.NewGuid();

        Assert.Equal("account_parent_invalid", Assert.Throws<DomainException>(() =>
            Account.Create(id, Guid.NewGuid(), Data(parent: id), Now, null)).Code);
    }

    [Fact]
    public void Edicion_y_baja_logica_conservan_creador_y_registran_actor()
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var account = Account.Create(Guid.NewGuid(), tenant, Data(), Now, creator);
        var parent = Guid.NewGuid();
        account.Update(new("02", " Bancos ", AccountKind.Liability, NormalBalance.Credit, parent, false), Now.AddMinutes(1), editor);
        account.SetActive(false, Now.AddMinutes(2), editor);

        Assert.Equal(tenant, account.TenantId);
        Assert.Equal(creator, account.CreatedBy);
        Assert.Equal(Now, account.CreatedAtUtc);
        Assert.Equal(editor, account.UpdatedBy);
        Assert.Equal(Now.AddMinutes(2), account.UpdatedAtUtc);
        Assert.False(account.IsActive);
        Assert.False(account.AllowsPosting);
        Assert.Equal(parent, account.ParentId);
        Assert.Equal("Bancos", account.Name);
        Assert.Equal(AccountKind.Liability, account.Kind);
        Assert.Equal(NormalBalance.Credit, account.NormalBalance);
    }

    [Fact]
    public void Edicion_invalida_no_modifica_parcialmente_la_entidad()
    {
        var account = Account.Create(Guid.NewGuid(), Guid.NewGuid(), Data(), Now, null);

        Assert.Throws<DomainException>(() => account.Update(Data("OTRA") with { Name = "" }, Now.AddHours(1), null));
        Assert.Equal("01.01", account.Code);
        Assert.Null(account.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("Asset", "Debit", true)]
    [InlineData("liability", "credit", true)]
    [InlineData("Equity", "Credit", true)]
    [InlineData("Income", "Credit", true)]
    [InlineData("Expense", "Debit", true)]
    [InlineData("1", "Debit", false)]
    [InlineData("Asset", "1", false)]
    [InlineData("Asset, Liability", "Debit", false)]
    [InlineData("Otro", "Debit", false)]
    [InlineData(null, null, false)]
    public void Validador_acepta_solo_nombres_de_tipo_y_naturaleza(string? kind, string? balance, bool valid)
    {
        var request = new SaveAccountRequest("01", "Caja", kind!, balance!, null, true);

        Assert.Equal(valid, new SaveAccountRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Validador_rechaza_nulos_limites_y_padre_vacio()
    {
        var request = new SaveAccountRequest(null!, new string('x', 161), "Asset", "Debit", Guid.Empty, true);
        var result = new SaveAccountRequestValidator().Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
        Assert.Contains(result.Errors, e => e.PropertyName == "ParentId");
        Assert.False(new SaveAccountRequestValidator().Validate(request with { Code = new string('X', 41) }).IsValid);
    }

    [Fact]
    public void Modelo_relacional_usa_esquema_indice_unico_y_padre_compuesto_restrictivo()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, "Host=localhost;Database=unused", AccountingDbContext.Schema);
        using var db = new AccountingDbContext(options.Options, TenantSession.ForSystem());
        var entity = db.Model.FindEntityType(typeof(Account))!;

        Assert.Equal("accounting", entity.GetSchema());
        Assert.Equal("accounts", entity.GetTableName());
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "Code"]));
        var fk = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(["TenantId", "ParentId"], fk.Properties.Select(p => p.Name));
        Assert.Equal(["TenantId", "Id"], fk.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Politica_preparada_usa_RLS_estandar_con_force_y_with_check()
    {
        var sql = AccountingDatabaseSecurity.TenantIsolationSql();

        Assert.Contains(RlsSql.EnableTenantIsolation("accounting", "accounts"), sql, StringComparison.Ordinal);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(AccountsController.List), AccountingPermissions.AccountsRead)]
    [InlineData(nameof(AccountsController.Get), AccountingPermissions.AccountsRead)]
    [InlineData(nameof(AccountsController.Create), AccountingPermissions.AccountsWrite)]
    [InlineData(nameof(AccountsController.Update), AccountingPermissions.AccountsWrite)]
    [InlineData(nameof(AccountsController.ChangeStatus), AccountingPermissions.AccountsWrite)]
    public void Cada_endpoint_exige_el_permiso_correspondiente(string method, string permission)
    {
        var attribute = Assert.Single(typeof(AccountsController).GetMethod(method)!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), true).Cast<RequirePermissionAttribute>());

        Assert.Equal(permission, attribute.PermissionCode);
    }
}
