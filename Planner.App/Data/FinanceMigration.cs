using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Planner.App.Data;

public static class FinanceMigration
{
    public static void EnsureFinanceTables(PlannerDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS FinanceCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS Transactions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Amount REAL NOT NULL,
                    Currency TEXT NOT NULL DEFAULT 'SEK',
                    Date TEXT NOT NULL,
                    CategoryId INTEGER NOT NULL,
                    Note TEXT,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (CategoryId) REFERENCES FinanceCategories(Id)
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS SavingsCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS SavingsEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Category INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Currency TEXT NOT NULL DEFAULT 'UAH',
                    Balance REAL NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );");
            EnsureSavingsCategoriesPopulated(db);
            AddSavingsCategoryIdColumnIfNeeded(db);
            EnsureSavingsMonthlySnapshotsTable(db);
            AddCurrencyColumnIfNeeded(db);
        }
        catch
        {
        }
    }

    private static void AddCurrencyColumnIfNeeded(PlannerDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();
            var columns = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Transactions);";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    columns.Add(r.GetString(1));
            }
            if (columns.Count == 0) return;
            if (columns.Contains("Currency", StringComparer.OrdinalIgnoreCase)) return;
            db.Database.ExecuteSqlRaw("ALTER TABLE Transactions ADD COLUMN Currency TEXT NOT NULL DEFAULT 'SEK';");
        }
        catch
        {
        }
    }

    private static void EnsureSavingsCategoriesPopulated(PlannerDbContext db)
    {
        try
        {
            var count = db.SavingsCategories.Count();
            if (count > 0) return;
            db.Database.ExecuteSqlRaw("INSERT INTO SavingsCategories (Name, SortOrder) VALUES ('Крипта', 0), ('Наличка', 1), ('Банки', 2);");
        }
        catch { }
    }

    private static void AddSavingsCategoryIdColumnIfNeeded(PlannerDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();
            var columns = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(SavingsEntries);";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    columns.Add(r.GetString(1));
            }
            if (columns.Count == 0) return;
            if (columns.Contains("SavingsCategoryId", StringComparer.OrdinalIgnoreCase)) return;
            db.Database.ExecuteSqlRaw("ALTER TABLE SavingsEntries ADD COLUMN SavingsCategoryId INTEGER REFERENCES SavingsCategories(Id);");
            var ids = db.SavingsCategories.OrderBy(c => c.SortOrder).Select(c => c.Id).ToList();
            if (ids.Count >= 3)
            {
                db.Database.ExecuteSqlRaw("UPDATE SavingsEntries SET SavingsCategoryId = {0} WHERE Category = 0", ids[0]);
                db.Database.ExecuteSqlRaw("UPDATE SavingsEntries SET SavingsCategoryId = {0} WHERE Category = 1", ids[1]);
                db.Database.ExecuteSqlRaw("UPDATE SavingsEntries SET SavingsCategoryId = {0} WHERE Category = 2", ids[2]);
            }
            db.Database.ExecuteSqlRaw("UPDATE SavingsEntries SET SavingsCategoryId = (SELECT Id FROM SavingsCategories LIMIT 1) WHERE SavingsCategoryId IS NULL;");
        }
        catch { }
    }

    private static void EnsureSavingsMonthlySnapshotsTable(PlannerDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS SavingsMonthlySnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Year INTEGER NOT NULL,
                    Month INTEGER NOT NULL,
                    TotalAmountUah REAL NOT NULL,
                    RecordedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_SavingsMonthlySnapshots_Year_Month ON SavingsMonthlySnapshots(Year, Month);");
        }
        catch { }
    }
}
