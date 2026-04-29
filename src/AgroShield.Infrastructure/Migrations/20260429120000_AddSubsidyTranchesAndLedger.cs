using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    public partial class AddSubsidyTranchesAndLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Extend existing subsidies table ─────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "farmer_id",
                table: "subsidies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "crop_type",
                table: "subsidies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workflow_status",
                table: "subsidies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "subsidies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subsidies_workflow_status",
                table: "subsidies",
                column: "workflow_status");

            // ── subsidy_tranches ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "subsidy_tranches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    subsidy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    percent_of_total = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    amount_kzt = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unlock_condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    release_evidence_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subsidy_tranches", x => x.id);
                    table.ForeignKey(
                        name: "fk_subsidy_tranches_subsidies_subsidy_id",
                        column: x => x.subsidy_id,
                        principalTable: "subsidies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subsidy_tranches_subsidy_id_order",
                table: "subsidy_tranches",
                columns: new[] { "subsidy_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subsidy_tranches_status_unlock_condition",
                table: "subsidy_tranches",
                columns: new[] { "status", "unlock_condition" });

            // ── supply_chain_ledger ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supply_chain_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_chain_ledger", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supply_chain_ledger_batch_time",
                table: "supply_chain_ledger",
                columns: new[] { "batch_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supply_chain_ledger_hash",
                table: "supply_chain_ledger",
                column: "entry_hash");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "supply_chain_ledger");
            migrationBuilder.DropTable(name: "subsidy_tranches");
            migrationBuilder.DropIndex(name: "ix_subsidies_workflow_status", table: "subsidies");
            migrationBuilder.DropColumn(name: "completed_at", table: "subsidies");
            migrationBuilder.DropColumn(name: "workflow_status", table: "subsidies");
            migrationBuilder.DropColumn(name: "crop_type", table: "subsidies");
            migrationBuilder.DropColumn(name: "farmer_id", table: "subsidies");
        }
    }
}
