using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmNdviActiveArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "active_area_from_ndvi",
                table: "farms",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ndvi_updated_at",
                table: "farms",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active_area_from_ndvi",
                table: "farms");

            migrationBuilder.DropColumn(
                name: "ndvi_updated_at",
                table: "farms");
        }
    }
}
