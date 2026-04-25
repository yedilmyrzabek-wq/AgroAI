using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ndvi_mean",
                table: "farms",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subsidies_status_checked_at",
                table: "subsidies",
                columns: new[] { "status", "checked_at" },
                filter: "checked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_profiles_telegram_chat_id",
                table: "profiles",
                column: "telegram_chat_id",
                filter: "telegram_chat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plant_diagnoses_is_healthy_created_at",
                table: "plant_diagnoses",
                columns: new[] { "is_healthy", "created_at" },
                filter: "is_healthy = false");

            migrationBuilder.CreateIndex(
                name: "ix_farms_device_id",
                table: "farms",
                column: "device_id",
                filter: "device_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_farms_risk_score",
                table: "farms",
                column: "risk_score");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_farm_id_created_at",
                table: "alerts",
                columns: new[] { "farm_id", "created_at" },
                filter: "farm_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subsidies_status_checked_at",
                table: "subsidies");

            migrationBuilder.DropIndex(
                name: "ix_profiles_telegram_chat_id",
                table: "profiles");

            migrationBuilder.DropIndex(
                name: "ix_plant_diagnoses_is_healthy_created_at",
                table: "plant_diagnoses");

            migrationBuilder.DropIndex(
                name: "ix_farms_device_id",
                table: "farms");

            migrationBuilder.DropIndex(
                name: "ix_farms_risk_score",
                table: "farms");

            migrationBuilder.DropIndex(
                name: "ix_alerts_farm_id_created_at",
                table: "alerts");

            migrationBuilder.AlterColumn<decimal>(
                name: "ndvi_mean",
                table: "farms",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldPrecision: 5,
                oldScale: 4,
                oldNullable: true);
        }
    }
}
