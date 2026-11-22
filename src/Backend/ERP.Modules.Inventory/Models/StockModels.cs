using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Inventory.Contracts;

namespace ERP.Modules.Inventory.Models;

public sealed class StockBalance : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public decimal QuantityReserved { get; private set; }

    public static StockBalance Create(Guid tenantId, Guid productId, Guid warehouseId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        ProductId = productId,
        WarehouseId = warehouseId,
    };

    public void Adjust(decimal quantity)
    {
        if (quantity == 0m)
        {
            throw new ERP.BuildingBlocks.Domain.DomainException("stock_quantity_invalid",
                "La cantidad del movimiento debe ser distinta de cero.");
        }

        if (decimal.Round(quantity, 4) != quantity)
        {
            throw new ERP.BuildingBlocks.Domain.DomainException("stock_quantity_scale",
                "La cantidad admite como máximo 4 decimales.");
        }

        var adjusted = decimal.Round(QuantityOnHand + quantity, 4);
        if (adjusted < QuantityReserved)
        {
            throw new ERP.BuildingBlocks.Domain.DomainException("stock_insufficient",
                "No hay stock disponible suficiente para confirmar la salida.");
        }

        QuantityOnHand = adjusted;
    }

    public void Reserve(decimal quantity)
    {
        if (quantity <= 0m || decimal.Round(quantity, 4) != quantity)
        {
            throw new ERP.BuildingBlocks.Domain.DomainException("stock_reservation_invalid",
                "La cantidad reservada debe ser positiva y tener máximo 4 decimales.");
        }

        var reserved = decimal.Round(QuantityReserved + quantity, 4);
        if (reserved > QuantityOnHand)
        {
            throw new ERP.BuildingBlocks.Domain.DomainException("stock_insufficient",
                "No hay stock disponible suficiente para reservar.");
        }

        QuantityReserved = reserved;
    }
}

public sealed class InventoryMovement : ITenantOwned
{
    private readonly List<InventoryMovementLineEntity> _lines = [];
    private InventoryMovement() { }
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
    public IReadOnlyList<InventoryMovementLineEntity> Lines => _lines;

    public static InventoryMovement Create(Guid id, Guid tenantId, InventoryMovementRequest request)
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
        };

    public void AddLine(InventoryMovementLineEntity line) => _lines.Add(line);
    public void SetTotalCost(decimal cost) => TotalCost = cost;
}

public sealed class InventoryMovementLineEntity : ITenantOwned
{
    private InventoryMovementLineEntity() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid MovementId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? UnitCost { get; private set; }

    public static InventoryMovementLineEntity Create(Guid tenantId, Guid movementId, Guid productId, decimal quantity, decimal? unitCost) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MovementId = movementId,
            ProductId = productId,
            Quantity = quantity,
            UnitCost = unitCost,
        };
}
