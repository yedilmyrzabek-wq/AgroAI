using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    public partial class AddV3Schema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Farm extensions ───────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "bank_bin",
                table: "farms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_iin",
                table: "farms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "elevator_contract_id",
                table: "farms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ndvi_history_json",
                table: "farms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_farms_owner_iin",
                table: "farms",
                column: "owner_iin",
                filter: "owner_iin IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_farms_bank_bin",
                table: "farms",
                column: "bank_bin",
                filter: "bank_bin IS NOT NULL");

            // ── Anomaly extensions ────────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "graph_risk_score",
                table: "anomalies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "related_farm_ids",
                table: "anomalies",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ml_features_json",
                table: "anomalies",
                type: "jsonb",
                nullable: true);

            // ── Livestock ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "livestock",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farm_id = t.Column<Guid>(type: "uuid", nullable: false),
                    livestock_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    declared_count = t.Column<int>(type: "integer", nullable: false),
                    last_detected_count = t.Column<int>(type: "integer", nullable: true),
                    last_detected_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_image_url = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    anomaly_detected = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_livestock", x => x.id);
                    t.ForeignKey("fk_livestock_farms_farm_id", x => x.farm_id, "farms", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_livestock_farm_id", "livestock", "farm_id");

            // ── Livestock Ledger ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "livestock_ledger",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farm_id = t.Column<Guid>(type: "uuid", nullable: false),
                    livestock_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    count = t.Column<int>(type: "integer", nullable: false),
                    prev_hash = t.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_hash = t.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by_user_id = t.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_livestock_ledger", x => x.id);
                    t.ForeignKey("fk_livestock_ledger_farms_farm_id", x => x.farm_id, "farms", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_livestock_ledger_entry_hash", "livestock_ledger", "entry_hash", unique: true);
            migrationBuilder.CreateIndex("ix_livestock_ledger_farm_id_created_at", "livestock_ledger", ["farm_id", "created_at"]);

            // ── Fertilizer Recommendations ────────────────────────────────
            migrationBuilder.CreateTable(
                name: "fertilizer_recommendations",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farm_id = t.Column<Guid>(type: "uuid", nullable: false),
                    n_kg_per_ha = t.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    p_kg_per_ha = t.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    k_kg_per_ha = t.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    total_kg = t.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    estimated_cost_kzt = t.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    expected_yield_increase_pct = t.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    application_windows = t.Column<string>(type: "jsonb", nullable: true),
                    explanation_ru = t.Column<string>(type: "text", nullable: true),
                    model_version = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_fertilizer_recommendations", x => x.id);
                    t.ForeignKey("fk_fertilizer_recommendations_farms_farm_id", x => x.farm_id, "farms", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_fertilizer_recommendations_farm_id_created_at", "fertilizer_recommendations", ["farm_id", "created_at"]);

            // ── Supply Chain Batches ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supply_chain_batches",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_code = t.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    farm_id = t.Column<Guid>(type: "uuid", nullable: false),
                    crop_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    initial_weight_kg = t.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    current_weight_kg = t.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    harvest_date = t.Column<DateOnly>(type: "date", nullable: true),
                    current_holder_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "farm"),
                    current_holder_id = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active"),
                    anomaly_detected = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_supply_chain_batches", x => x.id);
                    t.ForeignKey("fk_supply_chain_batches_farms_farm_id", x => x.farm_id, "farms", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_supply_chain_batches_batch_code", "supply_chain_batches", "batch_code", unique: true);
            migrationBuilder.CreateIndex("ix_supply_chain_batches_farm_id_status", "supply_chain_batches", ["farm_id", "status"]);

            // ── Supply Chain Transitions ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supply_chain_transitions",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = t.Column<Guid>(type: "uuid", nullable: false),
                    from_node_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_node_id = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_node_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    to_node_id = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    weight_kg = t.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    transferred_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = t.Column<string>(type: "text", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_supply_chain_transitions", x => x.id);
                    t.ForeignKey("fk_supply_chain_transitions_batches_batch_id", x => x.batch_id, "supply_chain_batches", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_supply_chain_transitions_batch_id_transferred_at", "supply_chain_transitions", ["batch_id", "transferred_at"]);

            // ── Notification Subscriptions ────────────────────────────────
            migrationBuilder.CreateTable(
                name: "notification_subscriptions",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = t.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = t.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    enabled = t.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_notification_subscriptions", x => x.id);
                    t.ForeignKey("fk_notification_subscriptions_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_notification_subscriptions_user_id_type", "notification_subscriptions", ["user_id", "notification_type"], unique: true);

            // ── Weekly Reports ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "weekly_reports",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = t.Column<Guid>(type: "uuid", nullable: false),
                    farm_ids = t.Column<Guid[]>(type: "uuid[]", nullable: false),
                    report_markdown = t.Column<string>(type: "text", nullable: false),
                    week_start = t.Column<DateOnly>(type: "date", nullable: false),
                    week_end = t.Column<DateOnly>(type: "date", nullable: false),
                    delivered_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_weekly_reports", x => x.id);
                    t.ForeignKey("fk_weekly_reports_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_weekly_reports_user_id_week_start", "weekly_reports", ["user_id", "week_start"]);

            // ── Knowledge Chunks ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_doc = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_url = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    chunk_index = t.Column<int>(type: "integer", nullable: false),
                    content = t.Column<string>(type: "text", nullable: false),
                    embedding_json = t.Column<string>(type: "jsonb", nullable: true),
                    created_at = t.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_knowledge_chunks", x => x.id);
                });

            migrationBuilder.CreateIndex("ix_knowledge_chunks_source_doc", "knowledge_chunks", "source_doc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("knowledge_chunks");
            migrationBuilder.DropTable("weekly_reports");
            migrationBuilder.DropTable("notification_subscriptions");
            migrationBuilder.DropTable("supply_chain_transitions");
            migrationBuilder.DropTable("supply_chain_batches");
            migrationBuilder.DropTable("fertilizer_recommendations");
            migrationBuilder.DropTable("livestock_ledger");
            migrationBuilder.DropTable("livestock");

            migrationBuilder.DropColumn("graph_risk_score", "anomalies");
            migrationBuilder.DropColumn("related_farm_ids", "anomalies");
            migrationBuilder.DropColumn("ml_features_json", "anomalies");

            migrationBuilder.DropColumn("bank_bin", "farms");
            migrationBuilder.DropColumn("owner_iin", "farms");
            migrationBuilder.DropColumn("elevator_contract_id", "farms");
            migrationBuilder.DropColumn("ndvi_history_json", "farms");
        }
    }
}
