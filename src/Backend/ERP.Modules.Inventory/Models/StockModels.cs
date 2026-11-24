using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Inventory.Contracts;

namespace ERP.Modules.Inventory.Models;

/// <summary>Efecto de una línea sobre el saldo: costo aplicado y saldo resultante (lo que muestra el kardex).</summary>
public sealed record StockChange(decimal UnitCost, decimal LineCost, decimal QuantityAfter, decimal AverageCostAfter, long Sequence);

/// <summary>
/// Saldo de un artículo en una bodega con su costo promedio ponderado móvil (ACUERDO-01).
/// Solo cambia a través de <see cref="Receive"/> y <see cref="Issue"/>: la existencia se calcula, nunca se digita.
/// La base también impide saldos negativos (CHECK) y el servicio bloquea la fila mientras la modifica.
/// </summary>
public sealed class StockBalance : ITenantOwned
{
    public const int QuantityScale = 4;
    public const int CostScale = 4;
    public const int AmountScale = 2;

    private StockBalance()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    /// <summary>Reservas: fuera de ACUERDO-01; siempre 0 en este incremento.</summary>
    public decimal QuantityReserved { get; private set; }

    /// <summary>Costo promedio vigente en USD, escala 4.</summary>
    public decimal AverageCost { get; private set; }

    /// <summary>Número del último movimiento aplicado a este saldo; ordena el kardex sin depender de la hora.</summary>
    public long LastSequence { get; private set; }

    public decimal Available => QuantityOnHand - QuantityReserved;

    public static StockBalance Create(Guid tenantId, Guid productId, Guid warehouseId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        ProductId = productId,
        WarehouseId = warehouseId,
    };

    /// <summary>Entrada: suma la cantidad y recalcula el promedio (existencia × promedio + cantidad × costo) / nueva existencia.</summary>
    public StockChange Receive(decimal quantity, decimal unitCost)
    {
        EnsureQuantity(quantity);
        EnsureUnitCost(unitCost);

        var quantityAfter = QuantityOnHand + quantity;
        AverageCost = decimal.Round((QuantityOnHand * AverageCost + quantity * unitCost) / quantityAfter, CostScale, MidpointRounding.AwayFromZero);
        QuantityOnHand = quantityAfter;
        LastSequence++;
        return new StockChange(unitCost, RoundAmount(quantity * unitCost), QuantityOnHand, AverageCost, LastSequence);
    }

    /// <summary>Salida: valorada al promedio vigente, que no cambia. Se rechaza completa si no alcanza la existencia.</summary>
    public StockChange Issue(decimal quantity)
    {
        EnsureQuantity(quantity);
        if (quantity > Available)
        {
            throw new DomainException("inventory_insufficient_stock",
                $"No hay existencia suficiente: disponible {Available:0.####}, solicitado {quantity:0.####}.");
        }

        QuantityOnHand -= quantity;
        LastSequence++;
        return new StockChange(AverageCost, RoundAmount(quantity * AverageCost), QuantityOnHand, AverageCost, LastSequence);
    }

    public static decimal RoundAmount(decimal amount) => decimal.Round(amount, AmountScale, MidpointRounding.AwayFromZero);

    public static bool IsValidQuantity(decimal quantity) => quantity > 0m && decimal.Round(quantity, QuantityScale) == quantity;

    public static bool IsValidUnitCost(decimal unitCost) => unitCost >= 0m && decimal.Round(unitCost, CostScale) == unitCost;

    private static void EnsureQuantity(decimal quantity)
    {
        if (!IsValidQuantity(quantity))
        {
            throw new DomainException("inventory_quantity_invalid", "La cantidad debe ser mayor que cero y tener máximo 4 decimales.");
        }
    }

    private static void EnsureUnitCost(decimal unitCost)
    {
        if (!IsValidUnitCost(unitCost))
        {
            throw new DomainException("inventory_unit_cost_invalid", "El costo unitario no puede ser negativo y admite máximo 4 decimales.");
        }
    }
}

/// <summary>Movimiento de un documento de Compras o Ventas. No se edita ni se borra (ACUERDO-01).</summary>
public sealed class InventoryMovement : ITenantOwned
{
    private readonly List<InventoryMovementLineEntity> _lines = [];

    private InventoryMovement()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string SourceModule { get; private set; } = string.Empty;

    public string SourceDocumentType { get; private set; } = string.Empty;

    public Guid SourceDocumentId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public InventoryMovementKind Kind { get; private set; }

    public DateOnly MovementDate { get; private set; }

    public decimal TotalCost { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public IReadOnlyList<InventoryMovementLineEntity> Lines => _lines;

    public static InventoryMovement Create(Guid id, Guid tenantId, InventoryMovementRequest request, DateTimeOffset nowUtc, Guid? userId)
        => new()
        {
            Id = id,
            TenantId = tenantId,
            IdempotencyKey = request.IdempotencyKey,
            SourceModule = request.SourceModule,
            SourceDocumentType = request.SourceDocumentType,
            SourceDocumentId = request.SourceDocumentId,
            WarehouseId = request.WarehouseId,
            Kind = request.Kind,
            MovementDate = request.MovementDate,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };

    public void AddLine(InventoryMovementLineEntity line)
    {
        _lines.Add(line);
        TotalCost += line.LineCost;
    }

    /// <summary>
    /// true si la solicitud repite exactamente este movimiento: mismo origen, bodega, tipo, fecha y líneas en el mismo orden.
    /// En una salida el costo lo calculó Inventario, así que solo se comparan artículo y cantidad.
    /// </summary>
    public bool HasSameContentAs(InventoryMovementRequest request)
    {
        if (SourceModule != request.SourceModule || SourceDocumentType != request.SourceDocumentType
            || SourceDocumentId != request.SourceDocumentId || WarehouseId != request.WarehouseId
            || Kind != request.Kind || MovementDate != request.MovementDate || _lines.Count != request.Lines.Count)
        {
            return false;
        }

        var ordered = _lines.OrderBy(l => l.LineNumber).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var saved = ordered[i];
            var requested = request.Lines[i];
            if (saved.ProductId != requested.ProductId || saved.Quantity != requested.Quantity
                || (Kind == InventoryMovementKind.Entrada && saved.UnitCost != requested.UnitCost))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Línea del movimiento con el costo aplicado y el saldo que dejó: es la fila del kardex.</summary>
public sealed class InventoryMovementLineEntity : ITenantOwned
{
    private InventoryMovementLineEntity()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid MovementId { get; private set; }

    /// <summary>Posición de la línea en la solicitud (desde 1).</summary>
    public int LineNumber { get; private set; }

    public Guid ProductId { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Entrada: costo de la compra. Salida: promedio vigente que aplicó Inventario.</summary>
    public decimal? UnitCost { get; private set; }

    /// <summary>Cantidad × costo unitario, USD con 2 decimales.</summary>
    public decimal LineCost { get; private set; }

    /// <summary>Existencia después de esta línea; null en movimientos registrados antes de INV-03.</summary>
    public decimal? QuantityAfter { get; private set; }

    /// <summary>Costo promedio después de esta línea; null en movimientos registrados antes de INV-03.</summary>
    public decimal? AverageCostAfter { get; private set; }

    /// <summary>Orden del movimiento dentro del saldo (artículo y bodega).</summary>
    public long Sequence { get; private set; }

    public static InventoryMovementLineEntity Create(Guid tenantId, Guid movementId, int lineNumber, Guid productId, decimal quantity, StockChange change) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MovementId = movementId,
            LineNumber = lineNumber,
            ProductId = productId,
            Quantity = quantity,
            UnitCost = change.UnitCost,
            LineCost = change.LineCost,
            QuantityAfter = change.QuantityAfter,
            AverageCostAfter = change.AverageCostAfter,
            Sequence = change.Sequence,
        };
}
