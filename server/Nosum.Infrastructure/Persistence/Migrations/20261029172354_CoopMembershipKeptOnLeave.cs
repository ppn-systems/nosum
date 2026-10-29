using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoopMembershipKeptOnLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coop_members_player_id",
                table: "coop_members");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "left_at",
                table: "coop_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_coop_members_coop_id_player_id",
                table: "coop_members",
                columns: new[] { "coop_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coop_members_player_id",
                table: "coop_members",
                column: "player_id",
                unique: true,
                filter: "left_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coop_members_coop_id_player_id",
                table: "coop_members");

            migrationBuilder.DropIndex(
                name: "ix_coop_members_player_id",
                table: "coop_members");

            migrationBuilder.DropColumn(
                name: "left_at",
                table: "coop_members");

            migrationBuilder.CreateIndex(
                name: "ix_coop_members_player_id",
                table: "coop_members",
                column: "player_id",
                unique: true);
        }
    }
}
