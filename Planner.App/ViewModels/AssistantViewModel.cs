using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Models;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class AssistantViewModel : ObservableObject, IDisposable
{
    private readonly AssistantRepositoryService _repo = new();
    private readonly AssistantLocalSettingsService _settings = new();
    private AssistantOrchestratorService? _orchestrator;
    private AssistantOrchestratorService Orchestrator => _orchestrator ??= new AssistantOrchestratorService();
    private bool _loadStarted;
    private int _lastConversationId = -1;

    [ObservableProperty] private ObservableCollection<AssistantMessageItemViewModel> _messages = new();
    [ObservableProperty] private ObservableCollection<AssistantTaskItemViewModel> _tasks = new();
    [ObservableProperty] private ObservableCollection<AssistantReportItemViewModel> _reports = new();
    [ObservableProperty] private string _draftMessage = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "gpt-4o-mini";
    [ObservableProperty] private string _endpoint = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private bool _allowFinanceData = true;
    [ObservableProperty] private bool _allowGoalsData = true;
    [ObservableProperty] private bool _allowRemindersData = true;
    [ObservableProperty] private string _keyHint = "";
    [ObservableProperty] private bool _isApiKeyReadOnly;

    public void StartLoad()
    {
        if (_loadStarted) return;
        _loadStarted = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(new Action(() => _ = LoadAsync()),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        else
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsBusy) return;
        var text = DraftMessage?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        IsBusy = true;
        StatusText = "Ассистент думает...";
        try
        {
            var result = await Orchestrator.SendUserMessageAsync(
                text,
                async (_, summary) =>
                {
                    return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        System.Windows.MessageBox.Show(
                            summary,
                            "Planner — подтверждение",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes);
                });
            DraftMessage = "";
            await ReloadMessagesAsync(result.Conversation.Id);
            await ReloadTasksAndReportsAsync();
            StatusText = "";
            GC.Collect(1, GCCollectionMode.Optimized);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void InsertQuickCommand(string commandText)
    {
        DraftMessage = commandText ?? "";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.SaveSettingsFromUi(new AssistantLlmSettings(
            ApiKey?.Trim() ?? "",
            Endpoint?.Trim() ?? "",
            Model?.Trim() ?? "",
            AllowFinanceData,
            AllowGoalsData,
            AllowRemindersData));
        var ui = _settings.GetUiSettings();
        ApiKey = ui.ApiKeyForEditing;
        KeyHint = ui.KeyHint;
        IsApiKeyReadOnly = ui.ApiKeyFromEnvironment;
        StatusText = "Настройки сохранены.";
    }

    private async Task LoadAsync()
    {
        try
        {
            var (ui, msgItems, taskItems, reportItems) = await Task.Run(async () =>
            {
                var u = _settings.GetUiSettings();
                var conv = await _repo.GetOrCreateMainConversationAsync();
                var m = await _repo.GetRecentMessagesAsync(conv.Id, 10);
                var t = await _repo.GetRecentTasksAsync(5);
                var r = await _repo.GetRecentReportsAsync(3);
                var mvm = m.Select(x => new AssistantMessageItemViewModel(x)).ToList();
                var tvm = t.Select(x => new AssistantTaskItemViewModel(x)).ToList();
                var rvm = r.Select(x => new AssistantReportItemViewModel(x)).ToList();
                return (u, mvm, tvm, rvm);
            });

            ApiKey = ui.ApiKeyForEditing;
            Endpoint = ui.Endpoint;
            Model = ui.Model;
            AllowFinanceData = ui.AllowFinanceData;
            AllowGoalsData = ui.AllowGoalsData;
            AllowRemindersData = ui.AllowRemindersData;
            KeyHint = ui.KeyHint;
            IsApiKeyReadOnly = ui.ApiKeyFromEnvironment;

            Messages = new ObservableCollection<AssistantMessageItemViewModel>(msgItems);
            Tasks = new ObservableCollection<AssistantTaskItemViewModel>(taskItems);
            Reports = new ObservableCollection<AssistantReportItemViewModel>(reportItems);
            _lastConversationId = (await _repo.GetOrCreateMainConversationAsync()).Id;
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка загрузки: " + ex.Message;
        }
    }

    private async Task ReloadMessagesAsync(int conversationId)
    {
        try
        {
            var list = await Task.Run(async () =>
            {
                var m = await _repo.GetRecentMessagesAsync(conversationId, 15);
                return m.Select(x => new AssistantMessageItemViewModel(x)).ToList();
            });
            
            // Обновляем коллекцию инкрементально вместо пересоздания
            var existingIds = new HashSet<int>(Messages.Select(x => x.Message.Id));
            var newItems = list.Where(x => !existingIds.Contains(x.Message.Id)).ToList();
            
            if (newItems.Count > 0)
            {
                foreach (var item in newItems)
                    Messages.Add(item);
                
                // Если сообщений больше чем надо, удаляем старые
                while (Messages.Count > 15)
                    Messages.RemoveAt(0);
            }
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка загрузки сообщений: " + ex.Message;
        }
    }

    private async Task ReloadTasksAndReportsAsync()
    {
        try
        {
            var (taskItems, reportItems) = await Task.Run(async () =>
            {
                var t = await _repo.GetRecentTasksAsync(8);
                var r = await _repo.GetRecentReportsAsync(4);
                return (
                    t.Select(x => new AssistantTaskItemViewModel(x)).ToList(),
                    r.Select(x => new AssistantReportItemViewModel(x)).ToList());
            });
            
            // Обновляем коллекции инкрементально
            var existingTaskIds = new HashSet<int>(Tasks.Select(x => x.Task.Id));
            var newTasks = taskItems.Where(x => !existingTaskIds.Contains(x.Task.Id)).ToList();
            foreach (var task in newTasks)
                Tasks.Add(task);
            while (Tasks.Count > 8)
                Tasks.RemoveAt(0);
                
            var existingReportIds = new HashSet<int>(Reports.Select(x => x.Report.Id));
            var newReports = reportItems.Where(x => !existingReportIds.Contains(x.Report.Id)).ToList();
            foreach (var report in newReports)
                Reports.Add(report);
            while (Reports.Count > 4)
                Reports.RemoveAt(0);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка загрузки задач: " + ex.Message;
        }
    }

    public void Dispose()
    {
        _orchestrator = null;
    }
}

public class AssistantMessageItemViewModel
{
    public AssistantMessage Message { get; }
    public bool IsUser => Message.Role == AssistantRole.User;
    public string RoleText => Message.Role == AssistantRole.User ? "Вы" : "Ассистент";
    public string CreatedText => Message.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm");

    public AssistantMessageItemViewModel(AssistantMessage message)
    {
        Message = message;
    }
}

public class AssistantTaskItemViewModel
{
    public AssistantTask Task { get; }
    public string StatusText => Task.Status switch
    {
        AssistantTaskStatus.Completed => "Выполнено",
        AssistantTaskStatus.Failed => "Ошибка",
        _ => "В процессе"
    };

    public AssistantTaskItemViewModel(AssistantTask task)
    {
        Task = task;
    }
}

public class AssistantReportItemViewModel
{
    public AssistantReport Report { get; }
    public string KindText => Report.Kind switch
    {
        AssistantReportPeriodKind.Day => "День",
        AssistantReportPeriodKind.Week => "Неделя",
        AssistantReportPeriodKind.Month => "Месяц",
        _ => "Отчет"
    };
    public string DateText => Report.PeriodStart.ToString("dd.MM.yyyy");

    public AssistantReportItemViewModel(AssistantReport report)
    {
        Report = report;
    }
}
