using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Accounting.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normal_balance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allows_posting = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.UniqueConstraint("ak_accounts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_accounts_code", "code ~ '^[A-Z0-9._-]{1,40}$'");
                    table.CheckConstraint("ck_accounts_kind", "kind IN ('Asset', 'Liability', 'Equity', 'Income', 'Expense')");
                    table.CheckConstraint("ck_accounts_name", "length(btrim(name)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_accounts_normal_balance", "normal_balance IN ('Debit', 'Credit')");
                    table.CheckConstraint("ck_accounts_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_accounts_accounts_tenant_id_parent_id",
                        columns: x => new { x.tenant_id, x.parent_id },
                        principalSchema: "accounting",
                        principalTable: "accounts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_code",
                schema: "accounting",
                table: "accounts",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_parent_id",
                schema: "accounting",
                table: "accounts",
                columns: new[] { "tenant_id", "parent_id" });

            // ── Seguridad multi-tenant ──
            // Requiere app_security.* y el rol erp_runtime, creados por la migración inicial de Platform.
            migrationBuilder.Sql(RlsSql.GrantSchemaUsage("accounting"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("accounting", "accounts"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounting");
        }
    }
}
