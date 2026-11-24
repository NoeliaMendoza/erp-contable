using System.Net;
using System.Net.Http.Json;
using ERP.BuildingBlocks.Domain;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// ACC-03: el único camino de otro módulo hacia los asientos. Valida cuentas de la empresa activa,
/// partida doble, trazabilidad e idempotencia contra PostgreSQL real con el rol runtime.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingPostingTests(PostgresFixture db) : IAsyncLifetime
{
    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
    }

    public async ValueTask DisposeAsync()
    {
        _ana.Dispose();
        _bruno.Dispose();
        await _api.DisposeAsync();
    }

    private static SaveAccountRequest Account(string code, bool allowsPosting = true) =>
        new(code, "Cuenta de asientos", "Asset", "Debit", null, allowsPosting);

    private async Task<AccountResponse> CreateAccountAsync(HttpClient client, bool allowsPosting = true, bool isActive = true)
    {
        var code = "P" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        using var response = await client.PostAsJsonAsync("/api/v1/accounts", Account(code, allowsPosting), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
        if (!isActive)
        {
            using var status = await client.PatchAsJsonAsync($"/api/v1/accounts/{account.Id}/status", new { isActive = false }, Ct);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        }

        return account;
    }

    /// <summary>El servicio no tiene endpoint propio: se resuelve en un ámbito con la empresa ya confirmada.</summary>
    private async Task<PostingResult> PostAsync(Guid tenantId, PostingRequest request)
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(tenantId, []);
        return await scope.ServiceProvider.GetRequiredService<IAccountingPostingService>().PostAsync(request, Ct);
    }

    private static PostingRequest Request(string key, IReadOnlyList<PostingLine> lines, DateOnly? date = null, string description = "Venta de prueba") =>
        new(key, "sales", "invoice", Guid.CreateVersion7(), date ?? new DateOnly(2026, 3, 15), description, lines);

    private static string Key() => "test:" + Guid.NewGuid().ToString("N");

    private async Task<(int Entries, int Lines)> CountAsync(Guid tenantId, string key)
    {
        await using var connection = new NpgsqlConnection(db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using var context = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        context.Parameters.AddWithValue("tenant", tenantId.ToString());
        await context.ExecuteNonQueryAsync(Ct);
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM accounting.journal_entries WHERE idempotency_key = @key),
                   (SELECT count(*) FROM accounting.journal_entry_lines l
                     JOIN accounting.journal_entries e ON e.id = l.journal_entry_id
                    WHERE e.idempotency_key = @key)
            """, connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    [Fact]
    public async Task Asiento_balanceado_se_registra_con_lineas_numeradas_y_cuenta_del_plan()
    {
        // Ejemplo del acuerdo: 30 = 20 + 12 - 2 no cuadra; se usa el caso cuadrado 32 = 20 + 12.
        var caja = await CreateAccountAsync(_ana);
        var ventas = await CreateAccountAsync(_ana);
        var iva = await CreateAccountAsync(_ana);
        var key = Key();

        var result = await PostAsync(DevelopmentSeeder.TenantAndinaId, Request(key,
        [
            new(caja.Code, 32.20m, 0m, "Cobro"),
            new(ventas.Code, 0m, 28.00m, null),
            new(iva.Code, 0m, 4.20m, "IVA 15%"),
        ]));

        Assert.False(result.AlreadyProcessed);
        Assert.NotEqual(Guid.Empty, result.JournalEntryId);
        var entry = await _ana.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{result.JournalEntryId}", Ct);
        Assert.Equal(32.20m, entry!.TotalDebit);
        Assert.Equal(32.20m, entry.TotalCredit);
        Assert.Equal("Posted", entry.Status);
        Assert.Equal("sales", entry.SourceModule);
        Assert.Equal([1, 2, 3], entry.Lines.Select(l => l.LineNumber));
        Assert.Equal([caja.Id, ventas.Id, iva.Id], entry.Lines.Select(l => l.AccountId));
        Assert.Equal("IVA 15%", entry.Lines[2].Memo);
        Assert.Equal((1, 3), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task Misma_clave_y_mismo_contenido_devuelve_el_asiento_original_sin_duplicar()
    {
        var debe = await CreateAccountAsync(_ana);
        var haber = await CreateAccountAsync(_ana);
        var key = Key();
        var request = Request(key, [new(debe.Code, 10m, 0m, "x"), new(haber.Code, 0m, 10m, null)]);

        var first = await PostAsync(DevelopmentSeeder.TenantAndinaId, request);
        var retry = await PostAsync(DevelopmentSeeder.TenantAndinaId, request);

        Assert.False(first.AlreadyProcessed);
        Assert.True(retry.AlreadyProcessed);
        Assert.Equal(first.JournalEntryId, retry.JournalEntryId);
        Assert.Equal((1, 2), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task Misma_clave_con_contenido_distinto_es_conflicto_409()
    {
        var debe = await CreateAccountAsync(_ana);
        var haber = await CreateAccountAsync(_ana);
        var key = Key();
        var original = Request(key, [new(debe.Code, 10m, 0m, null), new(haber.Code, 0m, 10m, null)]);
        await PostAsync(DevelopmentSeeder.TenantAndinaId, original);

        foreach (var different in new[]
        {
            original with { Lines = [new(debe.Code, 11m, 0m, null), new(haber.Code, 0m, 11m, null)] },
            original with { Lines = [new(debe.Code, 10m, 0m, "otro memo"), new(haber.Code, 0m, 10m, null)] },
            original with { Lines = [new(haber.Code, 10m, 0m, null), new(debe.Code, 0m, 10m, null)] },
            original with { Description = "Otra descripcion" },
            original with { AccountingDate = new DateOnly(2026, 4, 1) },
            original with { SourceDocumentId = Guid.CreateVersion7() },
            original with { SourceModule = "purchases" },
        })
        {
            var conflict = await Assert.ThrowsAsync<ConflictException>(() => PostAsync(DevelopmentSeeder.TenantAndinaId, different));
            Assert.Equal("posting_idempotency_conflict", conflict.Code);
        }

        Assert.Equal((1, 2), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task Reintentos_simultaneos_con_la_misma_clave_dejan_un_solo_asiento()
    {
        var debe = await CreateAccountAsync(_ana);
        var haber = await CreateAccountAsync(_ana);
        var key = Key();
        var request = Request(key, [new(debe.Code, 7.50m, 0m, null), new(haber.Code, 0m, 7.50m, null)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostAsync(DevelopmentSeeder.TenantAndinaId, request)));

        Assert.Single(results.Select(r => r.JournalEntryId).Distinct());
        Assert.Equal((1, 2), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task La_clave_es_por_empresa_y_no_se_comparte_entre_empresas()
    {
        var andina = await CreateAccountAsync(_ana);
        var andinaHaber = await CreateAccountAsync(_ana);
        var pacifico = await CreateAccountAsync(_bruno);
        var pacificoHaber = await CreateAccountAsync(_bruno);
        var key = Key();

        var first = await PostAsync(DevelopmentSeeder.TenantAndinaId,
            Request(key, [new(andina.Code, 5m, 0m, null), new(andinaHaber.Code, 0m, 5m, null)]));
        var second = await PostAsync(DevelopmentSeeder.TenantPacificoId,
            Request(key, [new(pacifico.Code, 9m, 0m, null), new(pacificoHaber.Code, 0m, 9m, null)]));

        Assert.NotEqual(first.JournalEntryId, second.JournalEntryId);
        Assert.Equal((1, 2), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
        Assert.Equal((1, 2), await CountAsync(DevelopmentSeeder.TenantPacificoId, key));
        using var cruzado = await _ana.GetAsync($"/api/v1/journal-entries/{second.JournalEntryId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, cruzado.StatusCode);
    }

    [Fact]
    public async Task Cuenta_inexistente_inactiva_sin_movimientos_o_de_otra_empresa_se_rechaza()
    {
        var valida = await CreateAccountAsync(_ana);
        var inactiva = await CreateAccountAsync(_ana, isActive: false);
        var sinMovimientos = await CreateAccountAsync(_ana, allowsPosting: false);
        var ajena = await CreateAccountAsync(_bruno);
        var key = Key();

        foreach (var codigo in new[] { "NO-EXISTE-" + Guid.NewGuid().ToString("N")[..8], inactiva.Code, sinMovimientos.Code, ajena.Code })
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => PostAsync(DevelopmentSeeder.TenantAndinaId,
                Request(key, [new(valida.Code, 1m, 0m, null), new(codigo, 0m, 1m, null)])));
            Assert.Equal("posting_account_not_available", error.Code);
            Assert.IsNotType<ConflictException>(error);
        }

        Assert.Equal((0, 0), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task Las_reglas_de_partida_doble_y_formato_se_validan_antes_de_tocar_la_base()
    {
        var debe = await CreateAccountAsync(_ana);
        var haber = await CreateAccountAsync(_ana);
        var key = Key();

        var casos = new (string Code, PostingRequest Request)[]
        {
            ("journal_description_required", Request(key, [new(debe.Code, 1m, 0m, null), new(haber.Code, 0m, 1m, null)], description: "   ")),
            ("journal_min_lines", Request(key, [new(debe.Code, 1m, 0m, null)])),
            ("journal_line_negative", Request(key, [new(debe.Code, -1m, 0m, null), new(haber.Code, 0m, -1m, null)])),
            ("journal_line_side", Request(key, [new(debe.Code, 1m, 1m, null), new(haber.Code, 0m, 1m, null)])),
            ("journal_line_side", Request(key, [new(debe.Code, 0m, 0m, null), new(haber.Code, 0m, 1m, null)])),
            ("journal_line_scale", Request(key, [new(debe.Code, 1.005m, 0m, null), new(haber.Code, 0m, 1.005m, null)])),
            ("journal_unbalanced", Request(key, [new(debe.Code, 10m, 0m, null), new(haber.Code, 0m, 9m, null)])),
            ("posting_source_invalid", Request(key, [new(debe.Code, 1m, 0m, null), new(haber.Code, 0m, 1m, null)]) with { SourceModule = " " }),
            ("posting_source_invalid", Request(key, [new(debe.Code, 1m, 0m, null), new(haber.Code, 0m, 1m, null)]) with { SourceDocumentId = Guid.Empty }),
        };

        foreach (var (code, request) in casos)
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => PostAsync(DevelopmentSeeder.TenantAndinaId, request));
            Assert.Equal(code, error.Code);
        }

        Assert.Equal((0, 0), await CountAsync(DevelopmentSeeder.TenantAndinaId, key));
    }

    [Fact]
    public async Task El_reverso_sigue_pendiente_del_acuerdo_y_lo_declara()
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(DevelopmentSeeder.TenantAndinaId, []);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingPostingService>();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.ReverseAsync(Guid.CreateVersion7(), new DateOnly(2026, 3, 20), "error", Key(), Ct));

        Assert.Equal("journal_reverse_pending", error.Code);
    }
}
