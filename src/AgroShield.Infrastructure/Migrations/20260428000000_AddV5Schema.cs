using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    public partial class AddV5Schema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Users.AssignedRegion ──────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "assigned_region",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_assigned_region",
                table: "users",
                column: "assigned_region",
                filter: "assigned_region IS NOT NULL");

            // ── SupplyChainBatch freeze fields ────────────────────────────
            migrationBuilder.AddColumn<DateTime>(
                name: "frozen_at",
                table: "supply_chain_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "frozen_by",
                table: "supply_chain_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "freeze_reason",
                table: "supply_chain_batches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "unfrozen_at",
                table: "supply_chain_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "unfrozen_by",
                table: "supply_chain_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_supply_chain_batches_users_frozen_by",
                table: "supply_chain_batches",
                column: "frozen_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_supply_chain_batches_users_unfrozen_by",
                table: "supply_chain_batches",
                column: "unfrozen_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "ix_batches_status_farm",
                table: "supply_chain_batches",
                columns: new[] { "status", "farm_id" });

            // ── SupplyChainAuditLog ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supply_chain_audit_log",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = t.Column<Guid>(type: "uuid", nullable: false),
                    action = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    performed_by = t.Column<Guid>(type: "uuid", nullable: false),
                    performed_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    reason = t.Column<string>(type: "text", nullable: true),
                    metadata_json = t.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_supply_chain_audit_log", x => x.id);
                    t.ForeignKey(
                        "fk_supply_chain_audit_log_batches_batch_id",
                        x => x.batch_id, "supply_chain_batches", "id",
                        onDelete: ReferentialAction.Cascade);
                    t.ForeignKey(
                        "fk_supply_chain_audit_log_users_performed_by",
                        x => x.performed_by, "users", "id",
                        onDelete: ReferentialAction.Restrict);
                    t.CheckConstraint(
                        "ck_supply_chain_audit_log_action",
                        "action IN ('freeze','unfreeze','move','move_blocked','status_change','cluster_freeze')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_batch_time",
                table: "supply_chain_audit_log",
                columns: new[] { "batch_id", "performed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_action",
                table: "supply_chain_audit_log",
                columns: new[] { "action", "performed_at" },
                descending: new[] { false, true });

            // ── Anomaly extensions ────────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "frozen_batches_count",
                table: "anomalies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_freeze_at",
                table: "anomalies",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("last_freeze_at", "anomalies");
            migrationBuilder.DropColumn("frozen_batches_count", "anomalies");

            migrationBuilder.DropTable("supply_chain_audit_log");

            migrationBuilder.DropIndex("ix_batches_status_farm", "supply_chain_batches");
            migrationBuilder.DropForeignKey("fk_supply_chain_batches_users_frozen_by", "supply_chain_batches");
            migrationBuilder.DropForeignKey("fk_supply_chain_batches_users_unfrozen_by", "supply_chain_batches");
            migrationBuilder.DropColumn("unfrozen_by", "supply_chain_batches");
            migrationBuilder.DropColumn("unfrozen_at", "supply_chain_batches");
            migrationBuilder.DropColumn("freeze_reason", "supply_chain_batches");
            migrationBuilder.DropColumn("frozen_by", "supply_chain_batches");
            migrationBuilder.DropColumn("frozen_at", "supply_chain_batches");

            migrationBuilder.DropIndex("ix_users_assigned_region", "users");
            migrationBuilder.DropColumn("assigned_region", "users");
        }
    }
}
