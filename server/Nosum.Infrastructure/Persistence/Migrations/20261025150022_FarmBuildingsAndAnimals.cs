using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FarmBuildingsAndAnimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "farm_buildings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<int>(type: "integer", nullable: false),
                    map_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<byte>(type: "smallint", nullable: false),
                    completes_at_ms = table.Column<long>(type: "bigint", nullable: false),
                    rations = table.Column<int>(type: "integer", nullable: false),
                    settled_day = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_buildings", x => x.id);
                    table.ForeignKey(
                        name: "fk_farm_buildings_placed_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "placed_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "farm_animals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<byte>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    friendship = table.Column<int>(type: "integer", nullable: false),
                    petted_day = table.Column<int>(type: "integer", nullable: false),
                    produce_day = table.Column<int>(type: "integer", nullable: false),
                    collected_day = table.Column<int>(type: "integer", nullable: false),
                    bought_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_animals", x => x.id);
                    table.ForeignKey(
                        name: "fk_farm_animals_farm_buildings_building_id",
                        column: x => x.building_id,
                        principalTable: "farm_buildings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_farm_animals_building_id",
                table: "farm_animals",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_buildings_map_id_owner_id",
                table: "farm_buildings",
                columns: new[] { "map_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_farm_buildings_object_id",
                table: "farm_buildings",
                column: "object_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "farm_animals");

            migrationBuilder.DropTable(
                name: "farm_buildings");
        }
    }
}
