using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedNav = "Goals";

    [ObservableProperty] private bool _runAtStartup;

    public MainViewModel()
    {
        CurrentPage = new Views.GoalsPage();
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
        CurrentPage = page switch
        {
            "Goals" => new Views.GoalsPage(),
            "Reminders" => new Views.RemindersPage(),
            "Dashboard" => new Views.DashboardPage(),
            "Finance" => new Views.FinancePage(),
            _ => CurrentPage
        };
    }
}
