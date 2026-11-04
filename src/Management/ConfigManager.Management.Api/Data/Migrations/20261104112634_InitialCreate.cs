using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConfigManager.Management.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    namespace_key = table.Column<string>(type: "text", nullable: true),
                    environment_key = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_request_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer = table.Column<string>(type: "text", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_request_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    environment_key = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    author = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    base_version = table.Column<long>(type: "bigint", nullable: false),
                    applied_version = table.Column<long>(type: "bigint", nullable: true),
                    required_approvals = table.Column<int>(type: "integer", nullable: false),
                    operations = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "environments",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environments", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "flag_environment_configs",
                columns: table => new
                {
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    environment_key = table.Column<string>(type: "text", nullable: false),
                    flag_key = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flag_environment_configs", x => new { x.namespace_key, x.environment_key, x.flag_key });
                });

            migrationBuilder.CreateTable(
                name: "flags",
                columns: table => new
                {
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flags", x => new { x.namespace_key, x.key });
                });

            migrationBuilder.CreateTable(
                name: "namespace_policies",
                columns: table => new
                {
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    environment_key = table.Column<string>(type: "text", nullable: false),
                    require_change_request = table.Column<bool>(type: "boolean", nullable: false),
                    required_approvals = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_namespace_policies", x => new { x.namespace_key, x.environment_key });
                });

            migrationBuilder.CreateTable(
                name: "namespace_role_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_namespace_role_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "namespaces",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_namespaces", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "published_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    environment_key = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<string>(type: "text", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_published_snapshots", x => x.id);
                    table.UniqueConstraint("ak_published_snapshots_namespace_key_environment_key_version", x => new { x.namespace_key, x.environment_key, x.version });
                });

            migrationBuilder.CreateTable(
                name: "service_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    namespace_key = table.Column<string>(type: "text", nullable: false),
                    environment_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_access_grants", x => x.id);
                    table.UniqueConstraint("ak_service_access_grants_client_id_namespace_key_environment_k", x => new { x.client_id, x.namespace_key, x.environment_key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_at",
                table: "audit_entries",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_change_request_reviews_change_request_id",
                table: "change_request_reviews",
                column: "change_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_namespace_key_environment_key_status",
                table: "change_requests",
                columns: new[] { "namespace_key", "environment_key", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_namespace_role_bindings_namespace_key_subject_type_subject",
                table: "namespace_role_bindings",
                columns: new[] { "namespace_key", "subject_type", "subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "change_request_reviews");

            migrationBuilder.DropTable(
                name: "change_requests");

            migrationBuilder.DropTable(
                name: "environments");

            migrationBuilder.DropTable(
                name: "flag_environment_configs");

            migrationBuilder.DropTable(
                name: "flags");

            migrationBuilder.DropTable(
                name: "namespace_policies");

            migrationBuilder.DropTable(
                name: "namespace_role_bindings");

            migrationBuilder.DropTable(
                name: "namespaces");

            migrationBuilder.DropTable(
                name: "published_snapshots");

            migrationBuilder.DropTable(
                name: "service_access_grants");
        }
    }
}
