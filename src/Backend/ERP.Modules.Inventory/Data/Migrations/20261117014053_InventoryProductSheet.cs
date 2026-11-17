using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Inventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryProductSheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "barcode",
                schema: "inventory",
                table: "products",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "inventory",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cost",
                schema: "inventory",
                table: "products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "inventory",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_stock",
                schema: "inventory",
                table: "products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "tracks_inventory",
                schema: "inventory",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "vat_treatment",
                schema: "inventory",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.UniqueConstraint("ak_categories_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_barcode",
                schema: "inventory",
                table: "products",
                columns: new[] { "tenant_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_category_id",
                schema: "inventory",
                table: "products",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_products_categories_tenant_id_category_id",
                schema: "inventory",
                table: "products",
                columns: new[] { "tenant_id", "category_id" },
                principalSchema: "inventory",
                principalTable: "categories",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // ── Editado a mano: revisar en cada PR que toque este esquema ──
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("inventory", "categories"));

            // Nombre de categoría único por empresa sin distinguir mayúsculas (mismo upper() que usa CategoryService).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_categories_tenant_id_upper_name ON inventory.categories (tenant_id, upper(name));");

            // Filas existentes: los productos controlan inventario y los servicios no. El IVA "Standard" solo se usó
            // para rellenar datos de demostración; después se quita el valor por defecto para que nunca se asuma un IVA.
            migrationBuilder.Sql("UPDATE inventory.products SET tracks_inventory = (kind = 'Product');");
            migrationBuilder.Sql("ALTER TABLE inventory.products ALTER COLUMN vat_treatment DROP DEFAULT;");

            // Las mismas reglas del dominio, también en la base.
            migrationBuilder.Sql("""
                ALTER TABLE inventory.products
                    ADD CONSTRAINT ck_products_vat_treatment CHECK (vat_treatment IN ('Standard', 'ZeroRated', 'Exempt', 'NotSubject')),
                    ADD CONSTRAINT ck_products_cost_non_negative CHECK (cost IS NULL OR cost >= 0),
                    ADD CONSTRAINT ck_products_min_stock_non_negative CHECK (min_stock IS NULL OR min_stock >= 0),
                    ADD CONSTRAINT ck_products_service_without_stock CHECK (kind <> 'Service' OR NOT tracks_inventory),
                    ADD CONSTRAINT ck_products_min_stock_requires_tracking CHECK (min_stock IS NULL OR tracks_inventory);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE inventory.products
                    DROP CONSTRAINT IF EXISTS ck_products_vat_treatment,
                    DROP CONSTRAINT IF EXISTS ck_products_cost_non_negative,
                    DROP CONSTRAINT IF EXISTS ck_products_min_stock_non_negative,
                    DROP CONSTRAINT IF EXISTS ck_products_service_without_stock,
                    DROP CONSTRAINT IF EXISTS ck_products_min_stock_requires_tracking;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_products_categories_tenant_id_category_id",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_barcode",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_category_id",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "barcode",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "cost",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "min_stock",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tracks_inventory",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "vat_treatment",
                schema: "inventory",
                table: "products");
        }
    }
}
