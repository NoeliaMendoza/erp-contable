using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Inventory.Services;

internal static class SearchPattern
{
    /// <summary>Patrón ILIKE "contiene", escapando los comodines escritos por el usuario (\, %, _).</summary>
    public static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
                  .Replace("%", "\\%", StringComparison.Ordinal)
                  .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

internal static class UniqueViolation
{
    /// <summary>Respaldo ante carreras: dos solicitudes simultáneas pasan la verificación previa y la base rechaza la segunda.</summary>
    public static bool Is(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
