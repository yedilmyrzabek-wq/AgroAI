using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroShield.Infrastructure.Migrations
{
    public partial class AddRlsPolicies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── profiles ──────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE profiles ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY profiles_select ON profiles FOR SELECT
  USING (auth.uid() = user_id OR (auth.jwt() ->> 'role') = 'admin');");
            migrationBuilder.Sql(@"
CREATE POLICY profiles_insert ON profiles FOR INSERT
  WITH CHECK (auth.uid() = user_id OR (auth.jwt() ->> 'role') = 'admin');");
            migrationBuilder.Sql(@"
CREATE POLICY profiles_update ON profiles FOR UPDATE
  USING (auth.uid() = user_id OR (auth.jwt() ->> 'role') = 'admin');");

            // ── farms ─────────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE farms ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY farms_select ON farms FOR SELECT
  USING (owner_id = auth.uid() OR (auth.jwt() ->> 'role') IN ('inspector','admin'));");
            migrationBuilder.Sql(@"
CREATE POLICY farms_insert ON farms FOR INSERT
  WITH CHECK (owner_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");
            migrationBuilder.Sql(@"
CREATE POLICY farms_update ON farms FOR UPDATE
  USING (owner_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");
            migrationBuilder.Sql(@"
CREATE POLICY farms_delete ON farms FOR DELETE
  USING (owner_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");

            // ── sensors ───────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE sensors ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY sensors_select ON sensors FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY sensors_write ON sensors FOR ALL
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') = 'admin'));");

            // ── sensor_readings ───────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE sensor_readings ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY sensor_readings_select ON sensor_readings FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY sensor_readings_insert ON sensor_readings FOR INSERT
  WITH CHECK (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid())
    OR (auth.jwt() ->> 'role') = 'admin');");

            // ── animals ───────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE animals ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY animals_select ON animals FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY animals_write ON animals FOR ALL
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') = 'admin'));");

            // ── animal_activities ─────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE animal_activities ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY animal_activities_select ON animal_activities FOR SELECT
  USING (animal_id IN (
    SELECT a.id FROM animals a
    JOIN farms f ON f.id = a.farm_id
    WHERE f.owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");

            // ── subsidies ─────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE subsidies ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY subsidies_select ON subsidies FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY subsidies_write ON subsidies FOR ALL
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') = 'admin'));");

            // ── anomalies ─────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE anomalies ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY anomalies_select ON anomalies FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY anomalies_write ON anomalies FOR ALL
  USING ((auth.jwt() ->> 'role') IN ('inspector','admin'));");

            // ── plant_diagnoses ───────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE plant_diagnoses ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY plant_diagnoses_select ON plant_diagnoses FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY plant_diagnoses_insert ON plant_diagnoses FOR INSERT
  WITH CHECK (user_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");

            // ── supply_chain_nodes ────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE supply_chain_nodes ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY supply_chain_nodes_select ON supply_chain_nodes FOR SELECT
  USING (auth.uid() IS NOT NULL);");
            migrationBuilder.Sql(@"
CREATE POLICY supply_chain_nodes_write ON supply_chain_nodes FOR ALL
  USING ((auth.jwt() ->> 'role') = 'admin');");

            // ── alerts ────────────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE alerts ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY alerts_select ON alerts FOR SELECT
  USING (user_id = auth.uid() OR user_id IS NULL
      OR (auth.jwt() ->> 'role') = 'admin');");
            migrationBuilder.Sql(@"
CREATE POLICY alerts_update ON alerts FOR UPDATE
  USING (user_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");

            // ── recommendations ───────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE recommendations ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY recommendations_select ON recommendations FOR SELECT
  USING (farm_id IN (
    SELECT id FROM farms WHERE owner_id = auth.uid()
       OR (auth.jwt() ->> 'role') IN ('inspector','admin')));");
            migrationBuilder.Sql(@"
CREATE POLICY recommendations_write ON recommendations FOR ALL
  USING ((auth.jwt() ->> 'role') IN ('inspector','admin'));");

            // ── chat_sessions ─────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE chat_sessions ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY chat_sessions_all ON chat_sessions FOR ALL
  USING (user_id = auth.uid() OR (auth.jwt() ->> 'role') = 'admin');");

            // ── chat_messages ─────────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE chat_messages ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY chat_messages_all ON chat_messages FOR ALL
  USING (session_id IN (
    SELECT id FROM chat_sessions WHERE user_id = auth.uid()
       OR (auth.jwt() ->> 'role') = 'admin'));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tables =
            [
                "profiles", "farms", "sensors", "sensor_readings",
                "animals", "animal_activities", "subsidies", "anomalies",
                "plant_diagnoses", "supply_chain_nodes", "alerts",
                "recommendations", "chat_sessions", "chat_messages"
            ];
            foreach (var t in tables)
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
        }
    }
}
