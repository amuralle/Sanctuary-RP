using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingFixtures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseFixtures",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false),
                    PlacementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tint = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionX = table.Column<float>(type: "REAL", nullable: false),
                    PositionY = table.Column<float>(type: "REAL", nullable: false),
                    PositionZ = table.Column<float>(type: "REAL", nullable: false),
                    RotationX = table.Column<float>(type: "REAL", nullable: false),
                    RotationY = table.Column<float>(type: "REAL", nullable: false),
                    RotationZ = table.Column<float>(type: "REAL", nullable: false),
                    RotationW = table.Column<float>(type: "REAL", nullable: false),
                    Scale = table.Column<float>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseFixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseFixtures_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HouseFixtures_Items_ItemId_CharacterId",
                        columns: x => new { x.ItemId, x.CharacterId },
                        principalTable: "Items",
                        principalColumns: new[] { "Id", "CharacterId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseFixtures_HouseId",
                table: "HouseFixtures",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseFixtures_ItemId_CharacterId",
                table: "HouseFixtures",
                columns: new[] { "ItemId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseFixtures_PlacementId",
                table: "HouseFixtures",
                column: "PlacementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseFixtures");
        }
    }
}
