using System.Diagnostics;
using ERP.Api.Errors;
using ERP.Api.Observability;
using ERP.Api.Security;
using ERP.BuildingBlocks.Caching;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Caching;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Accounting;
using ERP.Modules.Inventory;
using ERP.Modules.Platform;
using ERP.Modules.Platform.Data;
using ERP.Modules.Purchases;
using ERP.Modules.Reporting;
using ERP.Modules.Sales;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Observabilidad ────────────────────────────────────────────────────────────────────────────
builder.Services.AddSerilog((_, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext());

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("erp-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.UseOtlpExporter();
}

// ── Errores y contrato HTTP ───────────────────────────────────────────────────────────────────
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi("v1");

// ── Contexto de tenant, seguridad y caché ─────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenantSession>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantSession>());
builder.Services.AddErpAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddErpAuthorization();

var redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redis;
        o.InstanceName = "erp:";
    });
}

builder.Services.AddScoped<ITenantCache, TenantCache>();

// ── Módulos MVC (cada responsable registra controladores y servicios en su módulo) ──────────────────────────
builder.Services
    .AddPlatformModule(builder.Configuration)
    .AddAccountingModule(builder.Configuration)
    .AddSalesModule(builder.Configuration)
    .AddPurchasesModule(builder.Configuration)
    .AddInventoryModule(builder.Configuration)
    .AddReportingModule(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PlatformDbContext>("postgresql", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Controladores MVC de todos los módulos (cada módulo registra su ensamblado en Add<Modulo>Module).
app.MapControllers();

app.Run();

/// <summary>Punto de entrada expuesto para WebApplicationFactory en pruebas de integración.</summary>
public partial class Program;
