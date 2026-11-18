namespace ERP.BuildingBlocks.Domain;

/// <summary>
/// Conflicto con el estado actual: duplicado (código ya usado en la empresa) o un registro que no puede
/// cambiar porque otros dependen de él. La API responde 409 con el <see cref="DomainException.Code"/>.
/// </summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);
