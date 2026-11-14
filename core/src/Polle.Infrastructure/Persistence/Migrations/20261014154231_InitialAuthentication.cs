using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ecocrop_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecocrop_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ecocrop_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecocrop_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_ecocrop_sessions_ecocrop_users_user_id",
                        column: x => x.user_id,
                        principalTable: "ecocrop_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ecocrop_sessions_expires_at_utc",
                table: "ecocrop_sessions",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_ecocrop_sessions_user_id",
                table: "ecocrop_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecocrop_users_email",
                table: "ecocrop_users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ecocrop_sessions");

            migrationBuilder.DropTable(
                name: "ecocrop_users");
        }
    }
}
