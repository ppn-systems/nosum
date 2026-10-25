using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatFriendsMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "friends_may_visit",
                table: "visit_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "blocked_players",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocked_players", x => new { x.owner_id, x.blocked_id });
                    table.CheckConstraint("ck_blocked_players_not_self", "owner_id <> blocked_id");
                    table.ForeignKey(
                        name: "fk_blocked_players_players_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocked_players_players_owner_id",
                        column: x => x.owner_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_reports_players_reported_id",
                        column: x => x.reported_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_reports_players_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friend_requests",
                columns: table => new
                {
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friend_requests", x => new { x.from_id, x.to_id });
                    table.CheckConstraint("ck_friend_requests_not_self", "from_id <> to_id");
                    table.ForeignKey(
                        name: "fk_friend_requests_players_from_id",
                        column: x => x.from_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_friend_requests_players_to_id",
                        column: x => x.to_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    friend_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friendships", x => new { x.player_id, x.friend_id });
                    table.CheckConstraint("ck_friendships_not_self", "player_id <> friend_id");
                    table.ForeignKey(
                        name: "fk_friendships_players_friend_id",
                        column: x => x.friend_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_friendships_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    args = table.Column<string[]>(type: "text[]", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_messages", x => x.id);
                    table.CheckConstraint("ck_mail_messages_coins", "coins >= 0");
                    table.ForeignKey(
                        name: "fk_mail_messages_players_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_report_lines",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<byte>(type: "smallint", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_nickname = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(640)", maxLength: 640, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_report_lines", x => new { x.report_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_chat_report_lines_chat_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "chat_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_attachment_items",
                columns: table => new
                {
                    mail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_attachment_items", x => new { x.mail_id, x.item_id });
                    table.CheckConstraint("ck_mail_attachment_items_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_mail_attachment_items_mail_messages_mail_id",
                        column: x => x.mail_id,
                        principalTable: "mail_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blocked_players_blocked_id",
                table: "blocked_players",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_reported_id_created_at",
                table: "chat_reports",
                columns: new[] { "reported_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_reporter_id",
                table: "chat_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_friend_requests_to_id",
                table: "friend_requests",
                column: "to_id");

            migrationBuilder.CreateIndex(
                name: "ix_friendships_friend_id",
                table: "friendships",
                column: "friend_id");

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_expires_at",
                table: "mail_messages",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_recipient_id_sent_at",
                table: "mail_messages",
                columns: new[] { "recipient_id", "sent_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocked_players");

            migrationBuilder.DropTable(
                name: "chat_report_lines");

            migrationBuilder.DropTable(
                name: "friend_requests");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "mail_attachment_items");

            migrationBuilder.DropTable(
                name: "chat_reports");

            migrationBuilder.DropTable(
                name: "mail_messages");

            migrationBuilder.DropColumn(
                name: "friends_may_visit",
                table: "visit_settings");
        }
    }
}
