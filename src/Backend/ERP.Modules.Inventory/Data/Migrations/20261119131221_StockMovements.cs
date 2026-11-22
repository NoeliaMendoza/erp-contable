using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Inventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class StockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_module = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_document_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    movement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                    table.UniqueConstraint("ak_inventory_movements_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balances", x => x.id);
                    table.UniqueConstraint("ak_stock_balances_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "inventory_movement_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_movement_lines_inventory_movements_tenant_id_move",
                        columns: x => new { x.tenant_id, x.movement_id },
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_lines_tenant_id_movement_id",
                schema: "inventory",
                table: "inventory_movement_lines",
                columns: new[] { "tenant_id", "movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id_idempotency_key",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_tenant_id_product_id_warehouse_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "product_id", "warehouse_id" },
                unique: true);

            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("inventory", "stock_balances"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("inventory", "inventory_movements"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("inventory", "inventory_movement_lines"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_movement_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "inventory");
        }
    }
}
