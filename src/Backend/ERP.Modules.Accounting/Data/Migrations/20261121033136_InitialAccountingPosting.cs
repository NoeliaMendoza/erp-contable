using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Accounting.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountingPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accounting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_module = table.Column<string>(type: "text", nullable: false),
                    source_document_type = table.Column<string>(type: "text", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.UniqueConstraint("ak_journal_entries_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "journal_entry_lines",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    memo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entry_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entry_lines_journal_entries_tenant_id_journal_entry",
                        columns: x => new { x.tenant_id, x.journal_entry_id },
                        principalSchema: "accounting",
                        principalTable: "journal_entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_idempotency_key",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_entry_lines",
                columns: new[] { "tenant_id", "journal_entry_id" });

            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("accounting", "journal_entries"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("accounting", "journal_entry_lines"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_entry_lines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "accounting");
        }
    }
}
