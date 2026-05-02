using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedNav = "Goals";

    [ObservableProperty] private bool _runAtStartup;

    private readonly Dictionary<string, object> _pageCache = new();

    public MainViewModel()
    {
        CurrentPage = GetOrCreatePage("Goals");
        RunAtStartup = StartupService.IsRunAtStartup();
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        StartupService.SetRunAtStartup(value);
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedNav = page;
        var next = GetOrCreatePage(page);
        if (next != null) CurrentPage = next;
    }

    private object? GetOrCreatePage(string page)
    {
        if (_pageCache.TryGetValue(page, out var cached)) return cached;
        object? created = page switch
        {
            "Goals" => new Views.GoalsPage(),
            "Reminders" => new Views.RemindersPage(),
            "Dashboard" => new Views.DashboardPage(),
            "Finance" => new Views.FinancePage(),
            "Assistant" => new Views.AssistantPage(),
            _ => null
        };
        if (created != null) _pageCache[page] = created;
        return created;
    }
}
