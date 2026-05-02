using Microsoft.EntityFrameworkCore;

namespace Planner.App.Data;

public static class AssistantMigration
{
    public static void EnsureAssistantTables(PlannerDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantConversations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL DEFAULT 'Основной чат',
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConversationId INTEGER NOT NULL,
                    Role INTEGER NOT NULL,
                    Content TEXT NOT NULL DEFAULT '',
                    MetadataJson TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (ConversationId) REFERENCES AssistantConversations(Id)
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantMemoryFacts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Key TEXT NOT NULL,
                    Value TEXT NOT NULL DEFAULT '',
                    Confidence REAL NOT NULL DEFAULT 1.0,
                    UpdatedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantTasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Kind TEXT NOT NULL DEFAULT '',
                    RequestText TEXT NOT NULL DEFAULT '',
                    Status INTEGER NOT NULL DEFAULT 0,
                    ResultText TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantReports (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Kind INTEGER NOT NULL,
                    PeriodStart TEXT NOT NULL,
                    Body TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL
                );");
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AssistantTelemetryEvents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventType TEXT NOT NULL DEFAULT '',
                    Payload TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );");

            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AssistantConversations_UpdatedAt ON AssistantConversations(UpdatedAt);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AssistantMessages_ConversationId_CreatedAt ON AssistantMessages(ConversationId, CreatedAt);");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_AssistantMemoryFacts_Key ON AssistantMemoryFacts(Key);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AssistantTasks_CreatedAt ON AssistantTasks(CreatedAt);");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_AssistantReports_Kind_PeriodStart ON AssistantReports(Kind, PeriodStart);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AssistantTelemetryEvents_CreatedAt ON AssistantTelemetryEvents(CreatedAt);");
            db.Database.ExecuteSqlRaw(@"
                INSERT INTO AssistantConversations (Title, IsArchived, CreatedAt, UpdatedAt)
                SELECT 'Основной чат', 0, datetime('now'), datetime('now')
                WHERE NOT EXISTS (SELECT 1 FROM AssistantConversations WHERE IsArchived = 0);");
        }
        catch
        {
        }
    }
}
