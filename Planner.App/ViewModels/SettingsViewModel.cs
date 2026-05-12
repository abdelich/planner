using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Data;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AssistantLocalSettingsService _settings = new();

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "gpt-4o-mini";
    [ObservableProperty] private string _endpoint = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private bool _allowFinanceData = true;
    [ObservableProperty] private bool _allowGoalsData = true;
    [ObservableProperty] private bool _allowRemindersData = true;
    [ObservableProperty] private bool _runAtStartup;
    [ObservableProperty] private string _keyHint = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _voiceHotkey = "Ctrl+Alt+Space";
    [ObservableProperty] private int _voiceMicrophoneDeviceNumber = -1;
    [ObservableProperty] private bool _voiceSpeakAssistantResponses = true;
    [ObservableProperty] private ObservableCollection<VoiceMicrophoneDevice> _voiceMicrophones = new();

    public string DiagnosticsPath => AssistantDiagnosticsService.LogPath;
    public string DatabasePath => PlannerDbContext.DbPath;

    public SettingsViewModel()
    {
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        var ui = _settings.GetUiSettings();
        ApiKey = ui.ApiKeyForEditing;
        Endpoint = ui.Endpoint;
        Model = ui.Model;
        AllowFinanceData = ui.AllowFinanceData;
        AllowGoalsData = ui.AllowGoalsData;
        AllowRemindersData = ui.AllowRemindersData;
        KeyHint = ui.KeyHint;
        RunAtStartup = StartupService.IsRunAtStartup();
        VoiceHotkey = ui.VoiceHotkey;
        VoiceSpeakAssistantResponses = ui.VoiceSpeakAssistantResponses;
        LoadMicrophones(ui.VoiceMicrophoneDeviceNumber);
        StatusText = "";
    }

    [RelayCommand]
    private void Save()
    {
        _settings.SaveSettingsFromUi(
            new AssistantLlmSettings(
                ApiKey?.Trim() ?? "",
                Endpoint?.Trim() ?? "",
                Model?.Trim() ?? "",
                AllowFinanceData,
                AllowGoalsData,
                AllowRemindersData),
            new AssistantVoiceSettings(
                VoiceHotkey?.Trim() ?? "",
                VoiceMicrophoneDeviceNumber,
                VoiceSpeakAssistantResponses));
        StartupService.SetRunAtStartup(RunAtStartup);
        Load();
        StatusText = "Настройки сохранены.";
    }

    [RelayCommand]
    private void ClearSavedApiKey()
    {
        _settings.ClearSavedApiKey();
        Load();
        StatusText = "Сохраненный API-ключ очищен.";
    }

    [RelayCommand]
    private void RefreshMicrophones()
    {
        LoadMicrophones(VoiceMicrophoneDeviceNumber);
        StatusText = VoiceMicrophones.Count == 0
            ? "Микрофоны не найдены."
            : "Список микрофонов обновлен.";
    }

    private void LoadMicrophones(int preferredDeviceNumber)
    {
        var devices = VoiceMicrophoneService.GetDevices().ToList();
        VoiceMicrophones = new ObservableCollection<VoiceMicrophoneDevice>(devices);
        VoiceMicrophoneDeviceNumber = devices.Any(x => x.DeviceNumber == preferredDeviceNumber)
            ? preferredDeviceNumber
            : devices.FirstOrDefault()?.DeviceNumber ?? -1;
    }
}
