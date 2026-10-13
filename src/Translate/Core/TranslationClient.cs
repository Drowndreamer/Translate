using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
namespace Translate.Core;
public sealed class TranslationException(string message) : Exception(message);
public sealed class TranslationClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public TranslationClient(HttpClient? http = null) { _ownsHttp = http is null; _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; }
    public static Uri NormalizeEndpoint(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("请填写有效的 API 地址，例如 https://api.deepseek.com。");
        var builder = new UriBuilder(uri);
        var p = uri.AbsolutePath.TrimEnd('/');
        builder.Path = p.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? p : p + "/chat/completions";
        return builder.Uri;
    }
    public async IAsyncEnumerable<string> TranslateAsync(string text, AppSettings settings, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        settings.Validate();
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先输入或选中中文或英文。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        var payload = new Dictionary<string, object> {
            ["model"] = settings.Model.Trim(), ["stream"] = true,
            ["messages"] = new[] {
                new { role = "system", content = TranslationLanguage.Prompt(text) },
                new { role = "user", content = text }
            }
        };
        if (settings.Provider == "DeepSeek" || NormalizeEndpoint(settings.BaseUrl).Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
            payload["thinking"] = new { type = "disabled" };
        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeEndpoint(settings.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new TranslationException(ErrorMessage(response.StatusCode));
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var document = JsonDocument.Parse(await reader.ReadToEndAsync(token));
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content) || string.IsNullOrEmpty(content.GetString()))
                throw new TranslationException("接口没有返回译文，请检查模型或重试。");
            yield return content.GetString()!;
            yield break;
        }
        bool hasText = false, ended = false;
        var data = new StringBuilder();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(token);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var frame = data.ToString(); data.Clear();
                    if (frame.Trim() == "[DONE]") { ended = true; break; }
                    using var json = ParseFrame(frame);
                    if (json.RootElement.TryGetProperty("error", out _)) throw new TranslationException("接口返回错误，请检查模型配置后重试。");
                    if (json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(content.GetString()))
                        { hasText = true; yield return content.GetString()!; }
                        if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                        {
                            if (reason.GetString() == "length") throw new TranslationException("译文达到模型长度限制，请缩短原文后重试。");
                            if (reason.GetString() == "content_filter") throw new TranslationException("接口未能完成这段内容的翻译。");
                            ended = true;
                        }
                    }
                }
                if (line is null) break;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            { if (data.Length > 0) data.Append('\n'); data.Append(line.AsSpan(5).TrimStart()); }
        }
        if (!hasText) throw new TranslationException("接口没有返回译文，请检查模型或重试。");
        if (!ended) throw new TranslationException("连接提前断开，译文可能不完整，请重试。");
    }
    private static JsonDocument ParseFrame(string frame)
    {
        try { return JsonDocument.Parse(frame); }
        catch (JsonException) { throw new TranslationException("接口响应格式不正确，请检查 API 地址。"); }
    }
    public static string ErrorMessage(HttpStatusCode status) => (int)status switch {
        401 or 403 => "API Key 无效或没有访问权限，请在设置中检查。",
        402 => "API 账户余额不足，请充值后重试。",
        404 => "未找到接口或模型，请检查 API 地址和模型名称。",
        429 => "请求过于频繁，请稍后重试。",
        400 or 422 => "请求参数不被接口接受，请检查模型名称和 API 地址。",
        >= 500 => "翻译服务暂时不可用，请稍后重试。",
        _ => $"翻译请求失败（HTTP {(int)status}），请检查配置后重试。"
    };
    public static string FriendlyError(Exception error) => error switch {
        OperationCanceledException => "请求超时，请检查网络或稍后重试。",
        HttpRequestException => "无法连接翻译服务，请检查网络和 API 地址。",
        TranslationException or ArgumentException => error.Message,
        _ => "操作未完成，请重试。"
    };
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
