using System.Windows;
using Planner.App.Data;

namespace Planner.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "Planner",
                    "crash.log"),
                $"{System.DateTime.Now:O} - {args.Exception}\n");
            args.Handled = false;
        };
        using var db = new PlannerDbContext();
        db.Database.EnsureCreated();
        GoalMigration.AddRecurringColumnsIfNeeded(db);
        GoalMigration.BackfillPeriodGoalStartDates(db);
        FinanceMigration.EnsureFinanceTables(db);
        NotesMigration.EnsurePeriodNotesTable(db);
        AssistantMigration.EnsureAssistantTables(db);
    }
}

