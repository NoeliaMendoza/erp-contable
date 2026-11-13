namespace ERP.BuildingBlocks.Domain;

/// <summary>
/// Violación de una regla de negocio. La API la traduce a ProblemDetails (422) con
/// <see cref="Code"/> estable para que el cliente pueda reaccionar sin analizar textos.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Recurso inexistente o no visible para el tenant activo (se responde 404, sin revelar existencia).</summary>
public sealed class NotFoundException(string resource, object id)
    : DomainException("not_found", $"{resource} '{id}' no existe o no está disponible para la empresa activa.");
