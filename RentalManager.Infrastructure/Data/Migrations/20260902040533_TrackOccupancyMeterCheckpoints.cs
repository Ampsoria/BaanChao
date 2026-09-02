using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackOccupancyMeterCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeterCheckpoint",
                columns: table => new
                {
                    MeterCheckpointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WaterReading = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ElectricReading = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterCheckpoint", x => x.MeterCheckpointId);
                    table.CheckConstraint("CK_MeterCheckpoint_Electric", "[ElectricReading] >= 0");
                    table.CheckConstraint("CK_MeterCheckpoint_Kind", "[Kind] IN ('MoveIn','MoveOut','ImportedBaseline')");
                    table.CheckConstraint("CK_MeterCheckpoint_Water", "[WaterReading] >= 0");
                    table.ForeignKey(
                        name: "FK_MeterCheckpoint_Room_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Room",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeterCheckpoint_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeterCheckpoint_RoomId_RecordedAt",
                table: "MeterCheckpoint",
                columns: new[] { "RoomId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MeterCheckpoint_TenantId_Kind",
                table: "MeterCheckpoint",
                columns: new[] { "TenantId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeterCheckpoint");
        }
    }
}
