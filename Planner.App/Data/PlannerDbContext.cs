using System.IO;
using Microsoft.EntityFrameworkCore;
using Planner.App.Models;

namespace Planner.App.Data;

public class PlannerDbContext : DbContext
{
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalCompletion> GoalCompletions => Set<GoalCompletion>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ReminderCompletion> ReminderCompletions => Set<ReminderCompletion>();
    public DbSet<FinanceCategory> FinanceCategories => Set<FinanceCategory>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<SavingsCategory> SavingsCategories => Set<SavingsCategory>();
    public DbSet<SavingsEntry> SavingsEntries => Set<SavingsEntry>();
    public DbSet<SavingsMonthlySnapshot> SavingsMonthlySnapshots => Set<SavingsMonthlySnapshot>();

    public static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Planner", "planner.db");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var dir = Path.GetDirectoryName(DbPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        options.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoalCompletion>()
            .HasIndex(c => new { c.GoalId, c.Date });
        modelBuilder.Entity<ReminderCompletion>()
            .HasIndex(c => new { c.ReminderId, c.SlotDateTime });
        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.Date);
        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.CategoryId);
        modelBuilder.Entity<SavingsEntry>()
            .HasIndex(e => e.SavingsCategoryId);
        modelBuilder.Entity<SavingsMonthlySnapshot>()
            .HasIndex(s => new { s.Year, s.Month });
    }
}
