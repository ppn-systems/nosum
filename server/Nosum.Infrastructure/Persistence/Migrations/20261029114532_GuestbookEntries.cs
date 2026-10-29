using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuestbookEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guestbook_entries",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_number = table.Column<int>(type: "integer", nullable: false),
                    waters = table.Column<int>(type: "integer", nullable: false),
                    unread = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guestbook_entries", x => new { x.owner_id, x.visitor_id, x.day_number });
                    table.CheckConstraint("ck_guestbook_entries_not_self", "owner_id <> visitor_id");
                    table.CheckConstraint("ck_guestbook_entries_waters_positive", "waters > 0");
                    table.ForeignKey(
                        name: "fk_guestbook_entries_players_owner_id",
                        column: x => x.owner_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guestbook_entries_players_visitor_id",
                        column: x => x.visitor_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guestbook_entries_visitor_id",
                table: "guestbook_entries",
                column: "visitor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guestbook_entries");
        }
    }
}
