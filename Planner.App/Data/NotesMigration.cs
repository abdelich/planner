using Microsoft.EntityFrameworkCore;

namespace Planner.App.Data;

public static class NotesMigration
{
    public static void EnsurePeriodNotesTable(PlannerDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS PeriodNotes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Kind INTEGER NOT NULL,
                    PeriodStart TEXT NOT NULL,
                    Text TEXT NOT NULL DEFAULT '',
                    UpdatedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PeriodNotes_Kind_PeriodStart ON PeriodNotes(Kind, PeriodStart);");
        }
        catch
        {
        }
    }
}
