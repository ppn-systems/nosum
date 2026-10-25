using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminRoleBanMuteAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "banned_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "muted_until",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "role",
                table: "players",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "admin_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<byte>(type: "smallint", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_created_at",
                table: "admin_audit",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_target_id",
                table: "admin_audit",
                column: "target_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit");

            migrationBuilder.DropColumn(
                name: "banned_at",
                table: "players");

            migrationBuilder.DropColumn(
                name: "muted_until",
                table: "players");

            migrationBuilder.DropColumn(
                name: "role",
                table: "players");
        }
    }
}
