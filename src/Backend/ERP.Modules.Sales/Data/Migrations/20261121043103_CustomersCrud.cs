using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Sales.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomersCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identification_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
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
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.UniqueConstraint("ak_customers_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_identification_type_identification_numb",
                schema: "sales",
                table: "customers",
                columns: new[] { "tenant_id", "identification_type", "identification_number" },
                unique: true);

            migrationBuilder.Sql(RlsSql.GrantSchemaUsage("sales"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("sales", "customers"));
            migrationBuilder.Sql("""
                ALTER TABLE sales.customers
                    ADD CONSTRAINT ck_customers_identification_type
                        CHECK (identification_type IN ('Ruc', 'Cedula', 'Pasaporte', 'ConsumidorFinal')),
                    ADD CONSTRAINT ck_customers_identification_number
                        CHECK (length(btrim(identification_number)) BETWEEN 1 AND 20),
                    ADD CONSTRAINT ck_customers_name
                        CHECK (length(btrim(name)) BETWEEN 1 AND 200);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customers",
                schema: "sales");
        }
    }
}
