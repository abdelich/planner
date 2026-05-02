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

    /// <summary>Переменная окружения с ключом OpenAI (приоритетнее файла).</summary>
    public const string OpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    public static string? ReadEnvironmentApiKey()
    {
        var v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        v = Environment.GetEnvironmentVariable(OpenAiApiKeyEnvironmentVariable, EnvironmentVariableTarget.Machine);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>Настройки для запросов к LLM: ключ из env перекрывает файл.</summary>
    public AssistantLlmSettings GetEffectiveLlmSettings()
    {
        var fromFile = ReadFromFile();
        var envKey = ReadEnvironmentApiKey();
        if (!string.IsNullOrEmpty(envKey))
            return fromFile with { ApiKey = envKey };
        return fromFile;
    }

    /// <summary>Данные для экрана настроек (ключ из env не показывается в поле ввода).</summary>
    public AssistantUiSettings GetUiSettings()
    {
        var fromFile = ReadFromFile();
        var envKey = ReadEnvironmentApiKey();
        if (!string.IsNullOrEmpty(envKey))
        {
            return new AssistantUiSettings(
                ApiKeyForEditing: "",
                Endpoint: fromFile.Endpoint,
                Model: fromFile.Model,
                AllowFinanceData: fromFile.AllowFinanceData,
                AllowGoalsData: fromFile.AllowGoalsData,
                AllowRemindersData: fromFile.AllowRemindersData,
                ApiKeyFromEnvironment: true,
                KeyHint: $"Ключ берётся из переменной окружения {OpenAiApiKeyEnvironmentVariable} (приоритет над файлом).");
        }

        return new AssistantUiSettings(
            ApiKeyForEditing: fromFile.ApiKey,
            Endpoint: fromFile.Endpoint,
            Model: fromFile.Model,
            AllowFinanceData: fromFile.AllowFinanceData,
            AllowGoalsData: fromFile.AllowGoalsData,
            AllowRemindersData: fromFile.AllowRemindersData,
            ApiKeyFromEnvironment: false,
            KeyHint: "Ключ сохраняется в файле и шифруется средствами Windows (DPAPI) для текущего пользователя.");
    }

    /// <summary>Сохраняет настройки из UI. Пустое поле ключа не удаляет уже сохранённый ключ (если не задан OPENAI_API_KEY).</summary>
    public void SaveSettingsFromUi(AssistantLlmSettings ui)
    {
        var existing = ReadFromFile();
        var env = ReadEnvironmentApiKey();
        var typed = ui.ApiKey.Trim();
        string keyToPersist;
        if (!string.IsNullOrEmpty(env))
        {
            keyToPersist = string.IsNullOrEmpty(typed) ? existing.ApiKey : typed;
        }
        else
        {
            keyToPersist = string.IsNullOrEmpty(typed) ? existing.ApiKey : typed;
        }

        var merged = ui with { ApiKey = keyToPersist };
        var dto = AssistantLlmSettingsDto.FromDomain(merged);
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private AssistantLlmSettings ReadFromFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return Defaults();
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<AssistantLlmSettingsDto>(json);
            return dto?.ToDomain() ?? Defaults();
        }
        catch
        {
            return Defaults();
        }
    }

    private static AssistantLlmSettings Defaults()
        => new(
            ApiKey: "",
            Endpoint: "https://api.openai.com/v1/chat/completions",
            Model: "gpt-4o-mini",
            AllowFinanceData: true,
            AllowGoalsData: true,
            AllowRemindersData: true);

    private sealed class AssistantLlmSettingsDto
    {
        /// <summary>Устаревшее хранение в открытом виде (миграция).</summary>
        public string? ApiKey { get; set; }

        /// <summary>DPAPI, Base64.</summary>
        public string? ApiKeyProtected { get; set; }

        public string Endpoint { get; set; } = "";
        public string Model { get; set; } = "";
        public bool AllowFinanceData { get; set; }
        public bool AllowGoalsData { get; set; }
        public bool AllowRemindersData { get; set; }

        public AssistantLlmSettings ToDomain()
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

            return new AssistantLlmSettings(
                key,
                string.IsNullOrWhiteSpace(Endpoint) ? Defaults().Endpoint : Endpoint,
                string.IsNullOrWhiteSpace(Model) ? Defaults().Model : Model,
                AllowFinanceData,
                AllowGoalsData,
                AllowRemindersData);
        }

        public static AssistantLlmSettingsDto FromDomain(AssistantLlmSettings d)
        {
            string? plain = string.IsNullOrWhiteSpace(d.ApiKey) ? null : d.ApiKey.Trim();
            string? prot = null;
            if (!string.IsNullOrEmpty(plain))
                prot = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(plain)));

            return new AssistantLlmSettingsDto
            {
                ApiKey = null,
                ApiKeyProtected = prot,
                Endpoint = d.Endpoint,
                Model = d.Model,
                AllowFinanceData = d.AllowFinanceData,
                AllowGoalsData = d.AllowGoalsData,
                AllowRemindersData = d.AllowRemindersData
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
    string KeyHint);
