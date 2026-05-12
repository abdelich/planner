using Microsoft.EntityFrameworkCore;
using Planner.App.Data;
using Planner.App.Models;

namespace Planner.App.Services;

public class AssistantRepositoryService
{
    private const int MaxLoadedTextChars = 8000;
    private const string TrimSuffix = "\n\n[Текст сокращен при загрузке, чтобы не раздувать память приложения.]";

    public async Task<AssistantConversation> GetOrCreateMainConversationAsync()
    {
        await using var db = new PlannerDbContext();
        var conv = await db.AssistantConversations
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(x => !x.IsArchived);
        if (conv != null) return conv;

        conv = new AssistantConversation
        {
            Title = "Основной чат",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AssistantConversations.Add(conv);
        await db.SaveChangesAsync();
        return conv;
    }

    public async Task<List<AssistantMessage>> GetRecentMessagesAsync(int conversationId, int limit = 60)
    {
        await using var db = new PlannerDbContext();
        return await db.AssistantMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.Id)
            .Take(Math.Max(1, limit))
            .OrderBy(x => x.Id)
            .Select(x => new AssistantMessage
            {
                Id = x.Id,
                ConversationId = x.ConversationId,
                Role = x.Role,
                Content = x.Content.Length > MaxLoadedTextChars
                    ? x.Content.Substring(0, MaxLoadedTextChars) + TrimSuffix
                    : x.Content,
                MetadataJson = x.MetadataJson,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<AssistantMessage> AddMessageAsync(int conversationId, AssistantRole role, string content, string? metadataJson = null)
    {
        await using var db = new PlannerDbContext();
        content = TrimText(content);
        var msg = new AssistantMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content ?? "",
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow
        };
        db.AssistantMessages.Add(msg);
        var conv = await db.AssistantConversations.FindAsync(conversationId);
        if (conv != null) conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return msg;
    }

    public async Task ClearConversationMessagesAsync(int conversationId)
    {
        await using var db = new PlannerDbContext();
        await db.AssistantMessages
            .Where(x => x.ConversationId == conversationId)
            .ExecuteDeleteAsync();
        var conv = await db.AssistantConversations.FindAsync(conversationId);
        if (conv != null)
            conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpsertMemoryFactAsync(string key, string value, double confidence = 1.0)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        await using var db = new PlannerDbContext();
        var existing = await db.AssistantMemoryFacts.FirstOrDefaultAsync(x => x.Key == key);
        if (existing == null)
        {
            db.AssistantMemoryFacts.Add(new AssistantMemoryFact
            {
                Key = key.Trim(),
                Value = TrimText(value, 2000),
                Confidence = confidence,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Value = TrimText(value, 2000);
            existing.Confidence = confidence;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<AssistantMemoryFact>> GetMemoryFactsAsync(int limit = 20)
    {
        await using var db = new PlannerDbContext();
        return await db.AssistantMemoryFacts.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<AssistantTask> CreateTaskAsync(string kind, string requestText)
    {
        await using var db = new PlannerDbContext();
        var task = new AssistantTask
        {
            Kind = kind ?? "",
            RequestText = requestText ?? "",
            Status = AssistantTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AssistantTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task CompleteTaskAsync(int id, bool success, string resultText)
    {
        await using var db = new PlannerDbContext();
        var task = await db.AssistantTasks.FindAsync(id);
        if (task == null) return;
        task.Status = success ? AssistantTaskStatus.Completed : AssistantTaskStatus.Failed;
        task.ResultText = resultText;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<AssistantTask>> GetRecentTasksAsync(int limit = 30)
    {
        await using var db = new PlannerDbContext();
        return await db.AssistantTasks.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .Select(x => new AssistantTask
            {
                Id = x.Id,
                Kind = x.Kind,
                RequestText = x.RequestText.Length > MaxLoadedTextChars
                    ? x.RequestText.Substring(0, MaxLoadedTextChars) + TrimSuffix
                    : x.RequestText,
                Status = x.Status,
                ResultText = x.ResultText != null && x.ResultText.Length > MaxLoadedTextChars
                    ? x.ResultText.Substring(0, MaxLoadedTextChars) + TrimSuffix
                    : x.ResultText,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task SaveReportAsync(AssistantReportPeriodKind kind, DateTime periodStart, string body)
    {
        await using var db = new PlannerDbContext();
        body = TrimText(body);
        var keyDate = periodStart.Date;
        var existing = await db.AssistantReports.FirstOrDefaultAsync(x => x.Kind == kind && x.PeriodStart == keyDate);
        if (existing != null)
        {
            existing.Body = body ?? "";
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            db.AssistantReports.Add(new AssistantReport
            {
                Kind = kind,
                PeriodStart = keyDate,
                Body = body ?? "",
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<AssistantReport>> GetRecentReportsAsync(int limit = 15)
    {
        await using var db = new PlannerDbContext();
        return await db.AssistantReports.AsNoTracking()
            .OrderByDescending(x => x.PeriodStart)
            .ThenByDescending(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .Select(x => new AssistantReport
            {
                Id = x.Id,
                Kind = x.Kind,
                PeriodStart = x.PeriodStart,
                Body = x.Body.Length > MaxLoadedTextChars
                    ? x.Body.Substring(0, MaxLoadedTextChars) + TrimSuffix
                    : x.Body,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();
    }

    private static string TrimText(string? value, int maxChars = MaxLoadedTextChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + TrimSuffix;
    }
}
