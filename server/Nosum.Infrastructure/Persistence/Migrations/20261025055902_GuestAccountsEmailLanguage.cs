using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuestAccountsEmailLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_normalized_username",
                table: "players");

            migrationBuilder.DropColumn(
                name: "normalized_username",
                table: "players");

            migrationBuilder.DropColumn(
                name: "preferred_language",
                table: "players");

            migrationBuilder.DropColumn(
                name: "username",
                table: "players");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "players",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "players",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_guest",
                table: "players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "language",
                table: "players",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_active_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "players",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "registration_prompt_unlocked_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_is_guest_last_active_at",
                table: "players",
                columns: new[] { "is_guest", "last_active_at" });

            migrationBuilder.CreateIndex(
                name: "ix_players_normalized_email",
                table: "players",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_is_guest_last_active_at",
                table: "players");

            migrationBuilder.DropIndex(
                name: "ix_players_normalized_email",
                table: "players");

            migrationBuilder.DropColumn(
                name: "email",
                table: "players");

            migrationBuilder.DropColumn(
                name: "is_guest",
                table: "players");

            migrationBuilder.DropColumn(
                name: "language",
                table: "players");

            migrationBuilder.DropColumn(
                name: "last_active_at",
                table: "players");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "players");

            migrationBuilder.DropColumn(
                name: "registration_prompt_unlocked_at",
                table: "players");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "players",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_username",
                table: "players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                table: "players",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_players_normalized_username",
                table: "players",
                column: "normalized_username",
                unique: true);
        }
    }
}
