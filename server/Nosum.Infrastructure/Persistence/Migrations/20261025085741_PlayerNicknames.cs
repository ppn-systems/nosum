using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerNicknames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nickname",
                table: "players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "nickname_changed_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_nickname",
                table: "players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Players that existed before nicknames get a neutral guest name (digits only, so the client renders it with the localized
            // guest prefix); they can rename at once. The unique index below needs distinct values.
            migrationBuilder.Sql(
                "UPDATE players AS p SET nickname = n.name, normalized_nickname = n.name " +
                "FROM (SELECT id, 'guest-' || lpad(row_number() OVER (ORDER BY created_at, id)::text, 4, '0') AS name FROM players) AS n " +
                "WHERE p.id = n.id;");

            migrationBuilder.CreateIndex(
                name: "ix_players_normalized_nickname",
                table: "players",
                column: "normalized_nickname",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_normalized_nickname",
                table: "players");

            migrationBuilder.DropColumn(
                name: "nickname",
                table: "players");

            migrationBuilder.DropColumn(
                name: "nickname_changed_at",
                table: "players");

            migrationBuilder.DropColumn(
                name: "normalized_nickname",
                table: "players");
        }
    }
}
