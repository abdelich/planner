using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planner.App.Services;

public class AssistantLocalSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Planner",
        "assistant.settings.json");

    public const string OpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    public static event Action? SettingsChanged;

    public static string? ReadEnvironmentApiKey()
    {
        var v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable, EnvironmentVariableTarget.Machine);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    public AssistantLlmSettings GetEffectiveLlmSettings()
    {
        var fromFile = ReadStateFromFile().Llm;
        var envKey = ReadEnvironmentApiKey();
        if (string.IsNullOrWhiteSpace(fromFile.ApiKey) && !string.IsNullOrEmpty(envKey))
            return fromFile with { ApiKey = envKey };
        return fromFile;
    }

    public AssistantVoiceSettings GetVoiceSettings()
    {
        return ReadStateFromFile().Voice;
    }

    public AssistantUiSettings GetUiSettings()
    {
        var state = ReadStateFromFile();
        var fromFile = state.Llm;
        var voice = state.Voice;
        var envKey = ReadEnvironmentApiKey();
        if (!string.IsNullOrEmpty(envKey) && string.IsNullOrWhiteSpace(fromFile.ApiKey))
        {
            return new AssistantUiSettings(
                ApiKeyForEditing: "",
                Endpoint: fromFile.Endpoint,
                Model: fromFile.Model,
                AllowFinanceData: fromFile.AllowFinanceData,
                AllowGoalsData: fromFile.AllowGoalsData,
                AllowRemindersData: fromFile.AllowRemindersData,
                ApiKeyFromEnvironment: false,
                KeyHint: $"Если поле пустое, используется переменная окружения {OpenAiApiKeyEnvironmentVariable}. Вставленный здесь ключ сохранится и будет использоваться вместо нее.",
                VoiceHotkey: voice.Hotkey,
                VoiceMicrophoneDeviceNumber: voice.MicrophoneDeviceNumber,
                VoiceSpeakAssistantResponses: voice.SpeakAssistantResponses);
        }

        return new AssistantUiSettings(
            ApiKeyForEditing: fromFile.ApiKey,
            Endpoint: fromFile.Endpoint,
            Model: fromFile.Model,
            AllowFinanceData: fromFile.AllowFinanceData,
            AllowGoalsData: fromFile.AllowGoalsData,
            AllowRemindersData: fromFile.AllowRemindersData,
            ApiKeyFromEnvironment: false,
            KeyHint: string.IsNullOrEmpty(envKey)
                ? "Ключ сохраняется в файле и шифруется средствами Windows (DPAPI) для текущего пользователя."
                : $"Сохраненный здесь ключ используется вместо {OpenAiApiKeyEnvironmentVariable}. Поле можно очистить только вручную в файле настроек.",
            VoiceHotkey: voice.Hotkey,
            VoiceMicrophoneDeviceNumber: voice.MicrophoneDeviceNumber,
            VoiceSpeakAssistantResponses: voice.SpeakAssistantResponses);
    }

    public void SaveSettingsFromUi(AssistantLlmSettings ui)
    {
        var existing = ReadStateFromFile();
        var typed = ui.ApiKey.Trim();
        var keyToPersist = string.IsNullOrEmpty(typed) ? existing.Llm.ApiKey : typed;
        WriteState(ui with { ApiKey = keyToPersist }, existing.Voice);
    }

    public void SaveVoiceSettings(AssistantVoiceSettings voice)
    {
        var existing = ReadStateFromFile();
        WriteState(existing.Llm, NormalizeVoiceSettings(voice));
    }

    public void SaveSettingsFromUi(AssistantLlmSettings ui, AssistantVoiceSettings voice)
    {
        var existing = ReadStateFromFile();
        var typed = ui.ApiKey.Trim();
        var keyToPersist = string.IsNullOrEmpty(typed) ? existing.Llm.ApiKey : typed;
        WriteState(ui with { ApiKey = keyToPersist }, NormalizeVoiceSettings(voice));
    }

    public void ClearSavedApiKey()
    {
        var existing = ReadStateFromFile();
        WriteState(existing.Llm with { ApiKey = "" }, existing.Voice);
    }

    private static void WriteState(AssistantLlmSettings llm, AssistantVoiceSettings voice)
    {
        var dto = AssistantSettingsDto.FromDomain(llm, voice);
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, JsonOptions));
        SettingsChanged?.Invoke();
    }

    private AssistantSettingsState ReadStateFromFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return Defaults();
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<AssistantSettingsDto>(json);
            return dto?.ToDomain() ?? Defaults();
        }
        catch
        {
            return Defaults();
        }
    }

    private static AssistantSettingsState Defaults()
        => new(
            new AssistantLlmSettings(
                ApiKey: "",
                Endpoint: "https://api.openai.com/v1/chat/completions",
                Model: "gpt-4o-mini",
                AllowFinanceData: true,
                AllowGoalsData: true,
                AllowRemindersData: true),
            new AssistantVoiceSettings(
                Hotkey: "Ctrl+Alt+Space",
                MicrophoneDeviceNumber: -1,
                SpeakAssistantResponses: true));

    private static AssistantVoiceSettings NormalizeVoiceSettings(AssistantVoiceSettings voice)
    {
        return new AssistantVoiceSettings(
            string.IsNullOrWhiteSpace(voice.Hotkey) ? Defaults().Voice.Hotkey : voice.Hotkey.Trim(),
            voice.MicrophoneDeviceNumber,
            voice.SpeakAssistantResponses);
    }

    private sealed record AssistantSettingsState(AssistantLlmSettings Llm, AssistantVoiceSettings Voice);

    private sealed class AssistantSettingsDto
    {
        public string? ApiKey { get; set; }
        public string? ApiKeyProtected { get; set; }
        public string Endpoint { get; set; } = "";
        public string Model { get; set; } = "";
        public bool AllowFinanceData { get; set; }
        public bool AllowGoalsData { get; set; }
        public bool AllowRemindersData { get; set; }
        public string VoiceHotkey { get; set; } = "";
        public int VoiceMicrophoneDeviceNumber { get; set; } = -1;
        public bool VoiceSpeakAssistantResponses { get; set; } = true;

        public AssistantSettingsState ToDomain()
        {
            string key = "";
            if (!string.IsNullOrEmpty(ApiKeyProtected))
            {
                try
                {
                    var raw = Convert.FromBase64String(ApiKeyProtected);
                    key = Encoding.UTF8.GetString(Unprotect(raw));
                }
                catch
                {
                    key = "";
                }
            }
            else if (!string.IsNullOrEmpty(ApiKey))
            {
                key = ApiKey;
            }

            var defaults = Defaults();
            var llm = new AssistantLlmSettings(
                key,
                string.IsNullOrWhiteSpace(Endpoint) ? defaults.Llm.Endpoint : Endpoint,
                string.IsNullOrWhiteSpace(Model) ? defaults.Llm.Model : Model,
                AllowFinanceData,
                AllowGoalsData,
                AllowRemindersData);
            var voice = NormalizeVoiceSettings(new AssistantVoiceSettings(
                VoiceHotkey,
                VoiceMicrophoneDeviceNumber,
                VoiceSpeakAssistantResponses));
            return new AssistantSettingsState(llm, voice);
        }

        public static AssistantSettingsDto FromDomain(AssistantLlmSettings llm, AssistantVoiceSettings voice)
        {
            string? plain = string.IsNullOrWhiteSpace(llm.ApiKey) ? null : llm.ApiKey.Trim();
            string? prot = null;
            if (!string.IsNullOrEmpty(plain))
                prot = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(plain)));

            return new AssistantSettingsDto
            {
                ApiKey = null,
                ApiKeyProtected = prot,
                Endpoint = llm.Endpoint,
                Model = llm.Model,
                AllowFinanceData = llm.AllowFinanceData,
                AllowGoalsData = llm.AllowGoalsData,
                AllowRemindersData = llm.AllowRemindersData,
                VoiceHotkey = voice.Hotkey,
                VoiceMicrophoneDeviceNumber = voice.MicrophoneDeviceNumber,
                VoiceSpeakAssistantResponses = voice.SpeakAssistantResponses
            };
        }

        private static byte[] Protect(byte[] data)
            => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

        private static byte[] Unprotect(byte[] data)
            => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }
}

public record AssistantUiSettings(
    string ApiKeyForEditing,
    string Endpoint,
    string Model,
    bool AllowFinanceData,
    bool AllowGoalsData,
    bool AllowRemindersData,
    bool ApiKeyFromEnvironment,
    string KeyHint,
    string VoiceHotkey,
    int VoiceMicrophoneDeviceNumber,
    bool VoiceSpeakAssistantResponses);
