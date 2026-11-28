using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Accounting.Data.Migrations
{
    /// <summary>
    /// ACC-03 (#17): enlaza las líneas de los asientos con el plan de cuentas de la misma empresa,
    /// exige partida doble y orden de líneas en la base, y protege los asientos contra modificación
    /// y borrado revocando UPDATE y DELETE al rol de la aplicación.
    /// Las columnas nuevas se rellenan a partir del código de cuenta ya guardado; si alguna línea no
    /// se puede enlazar, la migración falla en lugar de dejar un asiento sin cuenta.
    /// </summary>
    /// <inheritdoc />
    public partial class AccountingPostingIntegrity : Migration
    {
        private const string Lines = "accounting.journal_entry_lines";
        private const string Accounts = "accounting.accounts";

        /// <summary>Tablas cuya política se suspende durante el relleno y se restablece a continuación.</summary>
        private static readonly string[] BackfillTables = [Lines, Accounts];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_entry_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.AlterColumn<string>(
                name: "account_code",
                schema: "accounting",
                table: "journal_entry_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "source_module",
                schema: "accounting",
                table: "journal_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_type",
                schema: "accounting",
                table: "journal_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            // Se agregan opcionales para poder deducir su valor de las filas existentes y recién
            // después se vuelven obligatorias; así no se inventa una cuenta por omisión.
            migrationBuilder.Sql($"""
                ALTER TABLE {Lines} ADD COLUMN account_id uuid NULL;
                ALTER TABLE {Lines} ADD COLUMN line_number integer NULL;
                """);

            migrationBuilder.Sql(BackfillSql());

            migrationBuilder.Sql($"""
                ALTER TABLE {Lines} ALTER COLUMN account_id SET NOT NULL;
                ALTER TABLE {Lines} ALTER COLUMN line_number SET NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_lines_tenant_id_account_id",
                schema: "accounting",
                table: "journal_entry_lines",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_lines_tenant_id_journal_entry_id_line_number",
                schema: "accounting",
                table: "journal_entry_lines",
                columns: new[] { "tenant_id", "journal_entry_id", "line_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entry_lines_not_negative",
                schema: "accounting",
                table: "journal_entry_lines",
                sql: "debit >= 0 AND credit >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entry_lines_number",
                schema: "accounting",
                table: "journal_entry_lines",
                sql: "line_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entry_lines_side",
                schema: "accounting",
                table: "journal_entry_lines",
                sql: "(debit > 0) <> (credit > 0)");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_accounting_date_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "accounting_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_source_module_source_document_typ",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "source_module", "source_document_type", "source_document_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_description",
                schema: "accounting",
                table: "journal_entries",
                sql: "length(btrim(description)) BETWEEN 1 AND 300");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_source",
                schema: "accounting",
                table: "journal_entries",
                sql: "length(btrim(source_module)) > 0 AND length(btrim(source_document_type)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_source_document",
                schema: "accounting",
                table: "journal_entries",
                sql: "source_document_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entry_lines_accounts_tenant_id_account_id",
                schema: "accounting",
                table: "journal_entry_lines",
                columns: new[] { "tenant_id", "account_id" },
                principalSchema: "accounting",
                principalTable: "accounts",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(AccountingDatabaseSecurity.PostingImmutabilitySql());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Se devuelve el CRUD completo que instaló RlsSql.EnableTenantIsolation.
            foreach (var table in AccountingDatabaseSecurity.ImmutableTables)
            {
                migrationBuilder.Sql($"GRANT UPDATE, DELETE ON accounting.{table} TO {RlsSql.RuntimeRole};");
            }

            migrationBuilder.DropForeignKey(
                name: "fk_journal_entry_lines_accounts_tenant_id_account_id",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_entry_lines_tenant_id_account_id",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_entry_lines_tenant_id_journal_entry_id_line_number",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entry_lines_not_negative",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entry_lines_number",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entry_lines_side",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_tenant_id_accounting_date_id",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_tenant_id_source_module_source_document_typ",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_description",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_source",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_source_document",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "account_id",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.DropColumn(
                name: "line_number",
                schema: "accounting",
                table: "journal_entry_lines");

            migrationBuilder.AlterColumn<string>(
                name: "account_code",
                schema: "accounting",
                table: "journal_entry_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "source_module",
                schema: "accounting",
                table: "journal_entries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "source_document_type",
                schema: "accounting",
                table: "journal_entries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_entry_lines",
                columns: new[] { "tenant_id", "journal_entry_id" });
        }

        /// <summary>
        /// Rellena las columnas nuevas de las líneas que ya existan. El enlace se deduce del código de
        /// cuenta guardado, así que el relleno lee <c>accounting.accounts</c> además de las líneas.
        /// Ambas tablas tienen RLS forzado, que alcanza también al propietario, y el rol de migraciones
        /// no fija empresa: sin suspender la política en las dos, la consulta no encontraría ninguna
        /// cuenta y la migración fallaría con datos existentes. La política se restablece a continuación,
        /// dentro de la misma transacción de la migración, así que nunca queda una tabla sin aislar.
        /// </summary>
        private static string BackfillSql() => $"""
            {Suspend()}

            UPDATE {Lines} AS l
               SET account_id = a.id
              FROM {Accounts} AS a
             WHERE a.tenant_id = l.tenant_id
               AND a.code = l.account_code
               AND l.account_id IS NULL;

            WITH ordenadas AS (
                SELECT id, row_number() OVER (PARTITION BY tenant_id, journal_entry_id ORDER BY id) AS posicion
                  FROM {Lines}
            )
            UPDATE {Lines} AS l
               SET line_number = ordenadas.posicion
              FROM ordenadas
             WHERE ordenadas.id = l.id
               AND l.line_number IS NULL;

            DO $$
            DECLARE sin_cuenta bigint;
            BEGIN
                SELECT count(*) INTO sin_cuenta FROM {Lines} WHERE account_id IS NULL;
                IF sin_cuenta > 0 THEN
                    RAISE EXCEPTION 'AccountingPostingIntegrity: % linea(s) de asiento no se pueden enlazar al plan de cuentas por su codigo. Revise {Lines} antes de migrar.', sin_cuenta;
                END IF;
            END $$;

            {Restore()}
            """;

        private static string Suspend() => string.Join(Environment.NewLine, BackfillTables.Select(table => $"""
            ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
            """));

        private static string Restore() => string.Join(Environment.NewLine, BackfillTables.Select(table => $"""
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            """));
    }
}
