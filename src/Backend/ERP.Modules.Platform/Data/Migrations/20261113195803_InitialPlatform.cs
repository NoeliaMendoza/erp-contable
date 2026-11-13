using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Modules.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_branches_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.UniqueConstraint("ak_roles_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_roles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxpayers",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruc = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    keeps_accounting = table.Column<bool>(type: "boolean", nullable: false),
                    special_taxpayer_resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_withholding_agent = table.Column<bool>(type: "boolean", nullable: false),
                    tax_regime_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxpayers", x => x.id);
                    table.UniqueConstraint("ak_taxpayers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_taxpayers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_features",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_features", x => new { x.tenant_id, x.feature_code });
                    table.ForeignKey(
                        name: "fk_tenant_features_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_memberships", x => x.id);
                    table.UniqueConstraint("ak_tenant_memberships_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_tenant_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "platform",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "platform",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "establishments",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxpayer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishments", x => x.id);
                    table.UniqueConstraint("ak_establishments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_establishments_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "platform",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_establishments_taxpayers_tenant_id_taxpayer_id",
                        columns: x => new { x.tenant_id, x.taxpayer_id },
                        principalSchema: "platform",
                        principalTable: "taxpayers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_roles",
                schema: "platform",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_roles", x => new { x.membership_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_membership_roles_roles_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "platform",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membership_roles_tenant_memberships_tenant_id_membership_id",
                        columns: x => new { x.tenant_id, x.membership_id },
                        principalSchema: "platform",
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "emission_points",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emission_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_emission_points_establishments_tenant_id_establishment_id",
                        columns: x => new { x.tenant_id, x.establishment_id },
                        principalSchema: "platform",
                        principalTable: "establishments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_code",
                schema: "platform",
                table: "branches",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emission_points_tenant_id_establishment_id_code",
                schema: "platform",
                table: "emission_points",
                columns: new[] { "tenant_id", "establishment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_establishments_tenant_id_branch_id",
                schema: "platform",
                table: "establishments",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_establishments_tenant_id_taxpayer_id_code",
                schema: "platform",
                table: "establishments",
                columns: new[] { "tenant_id", "taxpayer_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membership_roles_tenant_id_membership_id",
                schema: "platform",
                table: "membership_roles",
                columns: new[] { "tenant_id", "membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_roles_tenant_id_role_id",
                schema: "platform",
                table: "membership_roles",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_tenant_id_role_id",
                schema: "platform",
                table: "role_permissions",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                schema: "platform",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_taxpayers_tenant_id_ruc",
                schema: "platform",
                table: "taxpayers",
                columns: new[] { "tenant_id", "ruc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant_id_user_id",
                schema: "platform",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_user_id",
                schema: "platform",
                table: "tenant_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "platform",
                table: "users",
                column: "email",
                unique: true);

            // ── Seguridad multi-tenant (editado a mano; revisar en cada PR que toque este esquema) ──
            // Requiere que exista el rol de grupo erp_runtime (infrastructure/postgres/init).
            migrationBuilder.Sql(RlsSql.CreateSecurityFunctions());
            migrationBuilder.Sql(RlsSql.GrantSchemaUsage("platform"));

            // Tablas tenant-scoped estándar: denegación por defecto, solo filas del tenant del contexto.
            foreach (var table in new[] { "roles", "role_permissions", "membership_roles", "tenant_features", "branches", "taxpayers", "establishments", "emission_points" })
            {
                migrationBuilder.Sql(RlsSql.EnableTenantIsolation("platform", table));
            }

            // users: tabla GLOBAL de identidad (sin tenant_id, sin RLS). El runtime no puede borrar.
            migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE ON platform.users TO {RlsSql.RuntimeRole};");

            // tenants: visible si es el tenant activo o si el usuario autenticado es miembro.
            migrationBuilder.Sql($"""
                ALTER TABLE platform.tenants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE platform.tenants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_read ON platform.tenants FOR SELECT
                    USING (id = app_security.current_tenant_id()
                        OR EXISTS (SELECT 1 FROM platform.tenant_memberships m
                                   WHERE m.tenant_id = tenants.id AND m.user_id = app_security.current_user_id()));
                CREATE POLICY tenant_insert ON platform.tenants FOR INSERT
                    WITH CHECK (id = app_security.current_tenant_id());
                CREATE POLICY tenant_update ON platform.tenants FOR UPDATE
                    USING (id = app_security.current_tenant_id())
                    WITH CHECK (id = app_security.current_tenant_id());
                GRANT SELECT, INSERT, UPDATE ON platform.tenants TO {RlsSql.RuntimeRole};
                """);

            // tenant_memberships: lectura del tenant activo o de las propias membresías; escritura solo en el tenant activo.
            migrationBuilder.Sql($"""
                ALTER TABLE platform.tenant_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE platform.tenant_memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY membership_read ON platform.tenant_memberships FOR SELECT
                    USING (tenant_id = app_security.current_tenant_id() OR user_id = app_security.current_user_id());
                CREATE POLICY membership_insert ON platform.tenant_memberships FOR INSERT
                    WITH CHECK (tenant_id = app_security.current_tenant_id());
                CREATE POLICY membership_update ON platform.tenant_memberships FOR UPDATE
                    USING (tenant_id = app_security.current_tenant_id())
                    WITH CHECK (tenant_id = app_security.current_tenant_id());
                CREATE POLICY membership_delete ON platform.tenant_memberships FOR DELETE
                    USING (tenant_id = app_security.current_tenant_id());
                GRANT SELECT, INSERT, UPDATE, DELETE ON platform.tenant_memberships TO {RlsSql.RuntimeRole};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emission_points",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "membership_roles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_features",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "establishments",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_memberships",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "taxpayers",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "users",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "platform");

            migrationBuilder.Sql($"DROP SCHEMA IF EXISTS {RlsSql.SecuritySchema} CASCADE;");
        }
    }
}
