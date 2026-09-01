using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackFinancialCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Payment",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "Payment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AmountDueCollectedAt",
                table: "MoveOutSettlement",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmountDueCollectionMethod",
                table: "MoveOutSettlement",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "AmountDueCollectedAt",
                table: "MoveOutSettlement");

            migrationBuilder.DropColumn(
                name: "AmountDueCollectionMethod",
                table: "MoveOutSettlement");
        }
    }
}
