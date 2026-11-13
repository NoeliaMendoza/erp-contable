namespace ERP.BuildingBlocks.Tenancy;

/// <summary>
/// Marca una entidad como privada de un tenant. Las entidades que implementan esta interfaz
/// reciben filtro global en EF Core, asignación segura del tenant al escribir y deben tener
/// política RLS en PostgreSQL.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
