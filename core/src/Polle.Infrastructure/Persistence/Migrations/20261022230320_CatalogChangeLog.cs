using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_changes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    table_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    row_key = table.Column<string>(type: "jsonb", nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    cascade = table.Column<string>(type: "jsonb", nullable: true),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_changes", x => x.id);
                    table.CheckConstraint("ck_catalog_changes_action", "action IN ('insert', 'update', 'delete')");
                    table.ForeignKey(
                        name: "fk_catalog_changes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_changed_at_utc",
                schema: "public",
                table: "catalog_changes",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_table_name_changed_at_utc",
                schema: "public",
                table: "catalog_changes",
                columns: new[] { "table_name", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_user_id",
                schema: "public",
                table: "catalog_changes",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_changes",
                schema: "public");
        }
    }
}
