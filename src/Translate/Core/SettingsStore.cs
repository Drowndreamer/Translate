using System.Text.Json;
using Translate.Native;
namespace Translate.Core;
public sealed class SettingsStore
{
    private sealed class StoredSettings
    {
        public int Version { get; set; } = 1;
        public string Provider { get; set; } = "DeepSeek";
        public string BaseUrl { get; set; } = "https://api.deepseek.com";
        public string Model { get; set; } = "deepseek-flash";
        public string EncryptedApiKey { get; set; } = "";
        public bool RightClickEnabled { get; set; } = true;
        public int HoldMilliseconds { get; set; } = 300;
    }
    public string FilePath { get; }
    public string? LoadWarning { get; private set; }
    public SettingsStore(string? directory = null) => FilePath = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translate"), "settings.json");
    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var s = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(FilePath)) ?? throw new JsonException();
            return new() { Provider = s.Provider, BaseUrl = s.BaseUrl, Model = s.Model,
                ApiKey = string.IsNullOrEmpty(s.EncryptedApiKey) ? "" : Dpapi.Unprotect(s.EncryptedApiKey),
                RightClickEnabled = s.RightClickEnabled, HoldMilliseconds = Math.Clamp(s.HoldMilliseconds, 200, 2000) };
        }
        catch (Exception e) when (e is JsonException or IOException or System.ComponentModel.Win32Exception or FormatException or UnauthorizedAccessException)
        { LoadWarning = "原配置无法读取，请重新填写并保存。"; return new(); }
    }
    public void Save(AppSettings s)
    {
        var stored = new StoredSettings { Provider = s.Provider, BaseUrl = s.BaseUrl, Model = s.Model,
            EncryptedApiKey = string.IsNullOrEmpty(s.ApiKey) ? "" : Dpapi.Protect(s.ApiKey),
            RightClickEnabled = s.RightClickEnabled, HoldMilliseconds = s.HoldMilliseconds };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
