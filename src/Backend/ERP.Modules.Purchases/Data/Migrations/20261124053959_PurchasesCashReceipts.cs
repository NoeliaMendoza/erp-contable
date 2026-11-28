using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Purchases.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchasesCashReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_suppliers_tenant_id_id",
                schema: "purchases",
                table: "suppliers",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "purchases",
                schema: "purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchases", x => x.id);
                    table.UniqueConstraint("ak_purchases_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_purchases_number_nonempty", "length(btrim(number)) > 0");
                    table.CheckConstraint("ck_purchases_total_cost_nonnegative", "total_cost >= 0");
                    table.ForeignKey(
                        name: "fk_purchases_suppliers_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchases",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_lines",
                schema: "purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_lines_line_cost_nonnegative", "line_cost >= 0");
                    table.CheckConstraint("ck_purchase_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_purchase_lines_unit_cost_nonnegative", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_lines_purchases_tenant_id_purchase_id",
                        columns: x => new { x.tenant_id, x.purchase_id },
                        principalSchema: "purchases",
                        principalTable: "purchases",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_lines_purchase_product",
                schema: "purchases",
                table: "purchase_lines",
                columns: new[] { "tenant_id", "purchase_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchases_tenant_id_supplier_id",
                schema: "purchases",
                table: "purchases",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchases_tenant_number",
                schema: "purchases",
                table: "purchases",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.Sql(RlsSql.GrantSchemaUsage("purchases"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("purchases", "purchases"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("purchases", "purchase_lines"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_lines",
                schema: "purchases");

            migrationBuilder.DropTable(
                name: "purchases",
                schema: "purchases");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_suppliers_tenant_id_id",
                schema: "purchases",
                table: "suppliers");
        }
    }
}
