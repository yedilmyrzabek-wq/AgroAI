using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    public partial class AddLivestockLedgerEventType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "event_type",
                table: "livestock_ledger",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payload_json",
                table: "livestock_ledger",
                type: "jsonb",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("payload_json", "livestock_ledger");
            migrationBuilder.DropColumn("event_type", "livestock_ledger");
        }
    }
}
