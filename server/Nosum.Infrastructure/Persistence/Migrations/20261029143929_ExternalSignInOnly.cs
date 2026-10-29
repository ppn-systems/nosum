using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalSignInOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_normalized_email",
                table: "players");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "players");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "players");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "players",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "external_logins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_logins", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_logins_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_player_id",
                table: "external_logins",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_provider_subject",
                table: "external_logins",
                columns: new[] { "provider", "subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_logins");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "players",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "players",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "players",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_normalized_email",
                table: "players",
                column: "normalized_email",
                unique: true);
        }
    }
}
