using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Inventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryWarehousesAndAverageCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_movement_lines_tenant_id_movement_id",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.AddColumn<decimal>(
                name: "average_cost",
                schema: "inventory",
                table: "stock_balances",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "last_sequence",
                schema: "inventory",
                table: "stock_balances",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at_utc",
                schema: "inventory",
                table: "inventory_movements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "inventory",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "average_cost_after",
                schema: "inventory",
                table: "inventory_movement_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_cost",
                schema: "inventory",
                table: "inventory_movement_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "line_number",
                schema: "inventory",
                table: "inventory_movement_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity_after",
                schema: "inventory",
                table: "inventory_movement_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sequence",
                schema: "inventory",
                table: "inventory_movement_lines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_products_tenant_id_id",
                schema: "inventory",
                table: "products",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.UniqueConstraint("ak_warehouses_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            // ── Editado a mano: revisar en cada PR que toque este esquema ──
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("inventory", "warehouses"));

            // Datos existentes (ramas de Neon con movimientos de prueba). Las tablas tienen FORCE RLS y erp_migrator no
            // tiene BYPASSRLS: sin quitar FORCE un momento, estos UPDATE no verían ninguna fila. Se restaura al final.
            migrationBuilder.Sql("""
                ALTER TABLE inventory.stock_balances NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.inventory_movements NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.inventory_movement_lines NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.warehouses NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.products NO FORCE ROW LEVEL SECURITY;

                -- Cada bodega ya usada pasa a existir; la primera de cada empresa es la principal.
                INSERT INTO inventory.warehouses (id, tenant_id, code, name, is_default, is_active, created_at_utc)
                SELECT u.warehouse_id, u.tenant_id,
                       CASE WHEN u.n = 1 THEN 'PRINCIPAL' ELSE 'BODEGA-' || upper(left(u.warehouse_id::text, 8)) END,
                       CASE WHEN u.n = 1 THEN 'Bodega principal' ELSE 'Bodega ' || upper(left(u.warehouse_id::text, 8)) END,
                       u.n = 1, TRUE, now()
                FROM (
                    SELECT tenant_id, warehouse_id, row_number() OVER (PARTITION BY tenant_id ORDER BY warehouse_id) AS n
                    FROM (SELECT tenant_id, warehouse_id FROM inventory.stock_balances
                          UNION SELECT tenant_id, warehouse_id FROM inventory.inventory_movements) used
                ) u;

                UPDATE inventory.inventory_movements SET created_at_utc = now();

                UPDATE inventory.inventory_movement_lines l
                SET line_number = x.line_number, sequence = x.sequence, line_cost = round(coalesce(l.unit_cost, 0) * l.quantity, 2)
                FROM (
                    SELECT l2.id,
                           row_number() OVER (PARTITION BY l2.movement_id ORDER BY l2.id) AS line_number,
                           row_number() OVER (PARTITION BY l2.tenant_id, l2.product_id, m.warehouse_id ORDER BY m.id, l2.id) AS sequence
                    FROM inventory.inventory_movement_lines l2
                    JOIN inventory.inventory_movements m ON m.tenant_id = l2.tenant_id AND m.id = l2.movement_id
                ) x
                WHERE x.id = l.id;

                -- Sin historia de costos: el promedio inicial es el costo referencial de la ficha.
                UPDATE inventory.stock_balances s
                SET average_cost = coalesce(p.cost, 0),
                    last_sequence = coalesce((
                        SELECT max(l.sequence) FROM inventory.inventory_movement_lines l
                        JOIN inventory.inventory_movements m ON m.tenant_id = l.tenant_id AND m.id = l.movement_id
                        WHERE l.tenant_id = s.tenant_id AND l.product_id = s.product_id AND m.warehouse_id = s.warehouse_id), 0)
                FROM inventory.products p
                WHERE p.tenant_id = s.tenant_id AND p.id = s.product_id;

                ALTER TABLE inventory.stock_balances FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.inventory_movements FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.inventory_movement_lines FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.warehouses FORCE ROW LEVEL SECURITY;
                ALTER TABLE inventory.products FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id_warehouse_id",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_lines_tenant_id_movement_id_line_number",
                schema: "inventory",
                table: "inventory_movement_lines",
                columns: new[] { "tenant_id", "movement_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_lines_tenant_id_product_id_sequence",
                schema: "inventory",
                table: "inventory_movement_lines",
                columns: new[] { "tenant_id", "product_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_warehouses_tenant_id_default",
                schema: "inventory",
                table: "warehouses",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movement_lines_products_tenant_id_product_id",
                schema: "inventory",
                table: "inventory_movement_lines",
                columns: new[] { "tenant_id", "product_id" },
                principalSchema: "inventory",
                principalTable: "products",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_warehouses_tenant_id_warehouse_id",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "inventory",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_balances_products_tenant_id_product_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "product_id" },
                principalSchema: "inventory",
                principalTable: "products",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_balances_warehouses_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "inventory",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // La base también impide existencias negativas aunque falle el código (P0-2 de la auditoría).
            migrationBuilder.Sql("""
                ALTER TABLE inventory.stock_balances
                    ADD CONSTRAINT ck_stock_balances_on_hand_non_negative CHECK (quantity_on_hand >= 0),
                    ADD CONSTRAINT ck_stock_balances_reserved_valid CHECK (quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand),
                    ADD CONSTRAINT ck_stock_balances_average_cost_non_negative CHECK (average_cost >= 0);
                ALTER TABLE inventory.inventory_movement_lines
                    ADD CONSTRAINT ck_inventory_movement_lines_quantity_positive CHECK (quantity > 0),
                    ADD CONSTRAINT ck_inventory_movement_lines_line_cost_non_negative CHECK (line_cost >= 0);
                ALTER TABLE inventory.inventory_movements
                    ADD CONSTRAINT ck_inventory_movements_total_cost_non_negative CHECK (total_cost >= 0);
                ALTER TABLE inventory.warehouses
                    ADD CONSTRAINT ck_warehouses_default_is_active CHECK (NOT is_default OR is_active);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE inventory.stock_balances
                    DROP CONSTRAINT IF EXISTS ck_stock_balances_on_hand_non_negative,
                    DROP CONSTRAINT IF EXISTS ck_stock_balances_reserved_valid,
                    DROP CONSTRAINT IF EXISTS ck_stock_balances_average_cost_non_negative;
                ALTER TABLE inventory.inventory_movement_lines
                    DROP CONSTRAINT IF EXISTS ck_inventory_movement_lines_quantity_positive,
                    DROP CONSTRAINT IF EXISTS ck_inventory_movement_lines_line_cost_non_negative;
                ALTER TABLE inventory.inventory_movements DROP CONSTRAINT IF EXISTS ck_inventory_movements_total_cost_non_negative;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movement_lines_products_tenant_id_product_id",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_warehouses_tenant_id_warehouse_id",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_balances_products_tenant_id_product_id",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_balances_warehouses_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "ix_stock_balances_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_products_tenant_id_id",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_tenant_id_warehouse_id",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movement_lines_tenant_id_movement_id_line_number",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movement_lines_tenant_id_product_id_sequence",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropColumn(
                name: "average_cost",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropColumn(
                name: "last_sequence",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "average_cost_after",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropColumn(
                name: "line_cost",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropColumn(
                name: "line_number",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropColumn(
                name: "quantity_after",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "inventory",
                table: "inventory_movement_lines");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_lines_tenant_id_movement_id",
                schema: "inventory",
                table: "inventory_movement_lines",
                columns: new[] { "tenant_id", "movement_id" });
        }
    }
}
