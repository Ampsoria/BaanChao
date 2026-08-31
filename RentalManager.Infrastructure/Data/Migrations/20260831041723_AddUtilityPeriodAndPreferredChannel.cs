using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityPeriodAndPreferredChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredChannel",
                table: "Tenant",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Paper");

            migrationBuilder.AddColumn<string>(
                name: "UtilityPeriod",
                table: "Invoice",
                type: "char(7)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenant_Channel",
                table: "Tenant",
                sql: "[PreferredChannel] IN ('Line','Paper')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_UtilityPeriod",
                table: "Invoice",
                sql: "[UtilityPeriod] IS NULL OR [UtilityPeriod] = CONVERT(char(7), DATEADD(MONTH, -1, CONVERT(date, [BillingPeriod] + '-01', 126)), 126)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_UtilityUnits",
                table: "Invoice",
                sql: "[UtilityPeriod] IS NOT NULL OR ([WaterUnits] = 0 AND [ElectricUnits] = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenant_Channel",
                table: "Tenant");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_UtilityPeriod",
                table: "Invoice");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_UtilityUnits",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PreferredChannel",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "UtilityPeriod",
                table: "Invoice");
        }
    }
}
