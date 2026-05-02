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
    public DbSet<PeriodNote> PeriodNotes => Set<PeriodNote>();
    public DbSet<AssistantConversation> AssistantConversations => Set<AssistantConversation>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
    public DbSet<AssistantMemoryFact> AssistantMemoryFacts => Set<AssistantMemoryFact>();
    public DbSet<AssistantTask> AssistantTasks => Set<AssistantTask>();
    public DbSet<AssistantReport> AssistantReports => Set<AssistantReport>();
    public DbSet<AssistantTelemetryEvent> AssistantTelemetryEvents => Set<AssistantTelemetryEvent>();

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
        modelBuilder.Entity<PeriodNote>()
            .HasIndex(n => new { n.Kind, n.PeriodStart })
            .IsUnique();
        modelBuilder.Entity<AssistantConversation>()
            .HasIndex(c => c.UpdatedAt);
        modelBuilder.Entity<AssistantMessage>()
            .HasIndex(m => new { m.ConversationId, m.CreatedAt });
        modelBuilder.Entity<AssistantMemoryFact>()
            .HasIndex(m => m.Key)
            .IsUnique();
        modelBuilder.Entity<AssistantTask>()
            .HasIndex(t => t.CreatedAt);
        modelBuilder.Entity<AssistantReport>()
            .HasIndex(r => new { r.Kind, r.PeriodStart })
            .IsUnique();
        modelBuilder.Entity<AssistantTelemetryEvent>()
            .HasIndex(t => t.CreatedAt);
    }
}
