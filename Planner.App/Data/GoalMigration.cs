using Microsoft.EntityFrameworkCore;

namespace Planner.App.Data;

public static class GoalMigration
{
    public static void AddRecurringColumnsIfNeeded(PlannerDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();
            var columns = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Goals);";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    columns.Add(r.GetString(1));
            }
            if (columns.Count == 0) return;

            void AddColumn(string name, string typeAndDefault)
            {
                if (columns.Contains(name)) return;
                var sql = "ALTER TABLE Goals ADD COLUMN " + name + " " + typeAndDefault;
                db.Database.ExecuteSqlRaw(sql);
            }

            AddColumn("Category", "INTEGER NOT NULL DEFAULT 0");
            AddColumn("RecurrenceKind", "INTEGER NOT NULL DEFAULT 0");
            AddColumn("IntervalDays", "INTEGER NOT NULL DEFAULT 1");
        }
        catch
        {
        }
    }
}
