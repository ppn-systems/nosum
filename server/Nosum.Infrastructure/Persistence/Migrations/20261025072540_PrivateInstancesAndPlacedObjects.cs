using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivateInstancesAndPlacedObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_farm_plots",
                table: "farm_plots");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "farm_plots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing plots were one shared farm (ADR-012). The farm is private per player now (ADR-015): give each plot to the player
            // who last worked it, or to the oldest account when nobody is recorded, so no tilled or planted tile is lost. Plots of the
            // other players that shared the old farm are theirs only where they worked; that is the one lossy edge of this migration.
            migrationBuilder.Sql(
                """
                UPDATE farm_plots
                SET owner_id = COALESCE(
                    last_worked_by,
                    (SELECT id FROM players ORDER BY created_at, id LIMIT 1),
                    '00000000-0000-0000-0000-000000000000')
                WHERE map_id <> 'busstop' AND map_id <> 'forest';
                """);

            migrationBuilder.AddPrimaryKey(
                name: "pk_farm_plots",
                table: "farm_plots",
                columns: new[] { "map_id", "owner_id", "x", "y" });

            migrationBuilder.CreateTable(
                name: "placed_objects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    map_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    tile_x = table.Column<int>(type: "integer", nullable: false),
                    tile_y = table.Column<int>(type: "integer", nullable: false),
                    rotation = table.Column<int>(type: "integer", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placed_objects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_placed_objects_map_id_owner_id",
                table: "placed_objects",
                columns: new[] { "map_id", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "placed_objects");

            migrationBuilder.DropPrimaryKey(
                name: "pk_farm_plots",
                table: "farm_plots");

            // Back to one shared farm: where several owners tilled the same tile, keep the row of the lowest owner id.
            migrationBuilder.Sql(
                """
                DELETE FROM farm_plots a USING farm_plots b
                WHERE a.map_id = b.map_id AND a.x = b.x AND a.y = b.y AND a.owner_id > b.owner_id;
                """);

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "farm_plots");

            migrationBuilder.AddPrimaryKey(
                name: "pk_farm_plots",
                table: "farm_plots",
                columns: new[] { "map_id", "x", "y" });
        }
    }
}
