using ERP.BuildingBlocks.Security;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ERP.Api.Security;

internal sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;

/// <summary>Crea dinámicamente políticas <c>perm:{codigo}</c>: usuario autenticado + permiso en el tenant activo.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (PermissionPolicy.TryParse(policyName, out var code))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>El permiso solo se concede si hay tenant confirmado y el rol del usuario en ese tenant lo incluye.</summary>
internal sealed class PermissionAuthorizationHandler(ITenantContext tenant) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (tenant.HasTenant && tenant.HasPermission(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class AuthorizationSetup
{
    public static IServiceCollection AddErpAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
