using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmNdviMean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ndvi_mean",
                table: "farms",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ndvi_mean",
                table: "farms");
        }
    }
}
