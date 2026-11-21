
namespace ERP.Modules.Purchases.Models;

/// <summary>
/// Agrupa los datos comerciales y de identificación
/// que necesita un proveedor.
/// </summary>
public sealed record SupplierData(
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address
);
