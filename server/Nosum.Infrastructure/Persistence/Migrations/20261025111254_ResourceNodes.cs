using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResourceNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "node_instances",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    map_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_day = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_node_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resource_nodes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    map_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tile_x = table.Column<int>(type: "integer", nullable: false),
                    tile_y = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    variant = table.Column<short>(type: "smallint", nullable: false),
                    stage = table.Column<short>(type: "smallint", nullable: false),
                    hits = table.Column<short>(type: "smallint", nullable: false),
                    age_days = table.Column<int>(type: "integer", nullable: false),
                    spawned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_nodes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_node_instances_map_id_owner_id",
                table: "node_instances",
                columns: new[] { "map_id", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resource_nodes_map_id_owner_id_tile_x_tile_y",
                table: "resource_nodes",
                columns: new[] { "map_id", "owner_id", "tile_x", "tile_y" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "node_instances");

            migrationBuilder.DropTable(
                name: "resource_nodes");
        }
    }
}
