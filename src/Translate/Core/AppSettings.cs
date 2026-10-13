using System.Text.Json.Serialization;
namespace Translate.Core;
public sealed class AppSettings
{
    public string Provider { get; set; } = "DeepSeek";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    [JsonIgnore] public string ApiKey { get; set; } = "";
    public bool RightClickEnabled { get; set; } = true;
    public int HoldMilliseconds { get; set; } = 300;
    [JsonIgnore] public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
    public AppSettings Copy() => (AppSettings)MemberwiseClone();
    public void Validate()
    {
        TranslationClient.NormalizeEndpoint(BaseUrl);
        if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("请填写模型名称。");
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new ArgumentException("请填写 API Key。");
        if (HoldMilliseconds is < 200 or > 2000) throw new ArgumentException("长按时长需要在 0.2～2 秒之间。");
    }
}
