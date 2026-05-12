using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Planner.App.Data;

namespace Planner.App.Services;

public static class AssistantDiagnosticsService
{
    private static readonly object Lock = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Planner",
        "diagnostics.log");

    public static void LogMemory(string eventName, string? details = null)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var managedMb = GC.GetTotalMemory(false) / 1024d / 1024d;
            var privateMb = process.PrivateMemorySize64 / 1024d / 1024d;
            var workingSetMb = process.WorkingSet64 / 1024d / 1024d;
            var handleCount = 0;
            try { handleCount = process.HandleCount; } catch { }

            WriteLine(
                $"{DateTime.Now:O} | {eventName} | private={privateMb:N1}MB | workingSet={workingSetMb:N1}MB | managed={managedMb:N1}MB | threads={process.Threads.Count} | handles={handleCount} | {details ?? ""}");
        }
        catch
        {
        }
    }

    public static async Task LogAssistantDatabaseStatsAsync(string eventName)
    {
        try
        {
            await using var db = new PlannerDbContext();
            var messages = await QueryTextStatsAsync(db, "AssistantMessages", "Content");
            var tasks = await QueryTextStatsAsync(db, "AssistantTasks", "ResultText");
            var reports = await QueryTextStatsAsync(db, "AssistantReports", "Body");
            var telemetry = await QueryTextStatsAsync(db, "AssistantTelemetryEvents", "Payload");
            LogMemory(eventName,
                $"messages={messages}; tasks={tasks}; reports={reports}; telemetry={telemetry}");
        }
        catch (Exception ex)
        {
            LogMemory(eventName + "-failed", ex.Message);
        }
    }

    private static async Task<string> QueryTextStatsAsync(PlannerDbContext db, string table, string column)
    {
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*), COALESCE(MAX(LENGTH({column})), 0), COALESCE(SUM(LENGTH({column})), 0) FROM {table};";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return "count=0,max=0,total=0";

        return $"count={reader.GetInt64(0)},maxChars={reader.GetInt64(1)},totalChars={reader.GetInt64(2)}";
    }

    private static void WriteLine(string line)
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        lock (Lock)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}
