using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Purchases.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPurchasesSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchases");

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identification_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    identification_number = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_identification_number_nonempty", "length(btrim(identification_number)) > 0");
                    table.CheckConstraint("ck_suppliers_identification_type", "identification_type IN ('04', '05', '06', '08')");
                    table.CheckConstraint("ck_suppliers_name_nonempty", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_tenant_identification",
                schema: "purchases",
                table: "suppliers",
                columns: new[] { "tenant_id", "identification_type", "identification_number" },
                unique: true);

            // Requiere app_security.* y el rol erp_runtime, creados por la migración inicial de Platform.
            migrationBuilder.Sql(RlsSql.GrantSchemaUsage("purchases"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("purchases", "suppliers"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "purchases");
        }
    }
}
