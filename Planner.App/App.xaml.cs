using System.Windows;
using Planner.App.Data;

namespace Planner.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var db = new PlannerDbContext();
        db.Database.EnsureCreated();
        GoalMigration.AddRecurringColumnsIfNeeded(db);
        FinanceMigration.EnsureFinanceTables(db);
    }
}

