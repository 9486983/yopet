using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using yopet.Sdk;

namespace WebAnalyzerPlugin;

/// <summary>
/// 网页分析插件：输入 URL → 抓取内容 → AI 分析 → Markdown 报告
/// </summary>
[Plugin("网页分析器", Version = "1.0.0", Description = "抓取网页内容并用 AI 生成结构化 Markdown 报告")]
public class WebAnalyzerPlugin : PluginBase
{
    private const string KeyApiKey = "api_key";
    private const string KeyApiUrl = "api_url";
    private const string KeyModel = "model";

    private IPluginHost? _host;

    public override string Name => "网页分析器";

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // ── 配置定义 ──
        host.RegisterConfig(new PluginConfigSection
        {
            Title = "网页分析器",
            Emoji = "🌐",
            Fields = new()
            {
                new()
                {
                    Key = KeyApiKey,
                    Label = "API Key",
                    Type = PluginConfigFieldType.Password,
                    IsRequired = true,
                    Placeholder = "sk-...",
                    Description = "用于分析网页内容的 AI API Key（兼容 OpenAI/DeepSeek 格式）",
                },
                new()
                {
                    Key = KeyApiUrl,
                    Label = "API 接口",
                    Type = PluginConfigFieldType.String,
                    DefaultValue = "https://api.deepseek.com/v1/chat/completions",
                    Description = "Chat Completions API 地址",
                },
                new()
                {
                    Key = KeyModel,
                    Label = "模型",
                    Type = PluginConfigFieldType.String,
                    DefaultValue = "deepseek-chat",
                    Description = "模型名称，如 deepseek-chat、gpt-4o-mini 等",
                },
            },
        }, Name);

        // ── 设置入口 ──
        host.RegisterAction(new PluginAction
        {
            Name = "设置",
            Emoji = "⚙️",
            Group = "🌐 网页分析器",
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog("网页分析器");
                return Task.CompletedTask;
            },
        });

        // ── 分析网页 ──
        host.RegisterAction(new PluginAction
        {
            Name = "分析网页",
            Emoji = "🌐",
            Description = "抓取网页内容并用 AI 生成 Markdown 分析报告",
            Group = "🌐 网页分析器",
            Target = ActionTarget.ContextMenu,
            Callback = async () => await AnalyzeUrl(),
        });

        host.Log("网页分析器插件已加载");
    }

    private async Task AnalyzeUrl()
    {
        if (_host == null) return;

        // ── 检查 API Key ──
        var apiKey = _host.GetConfig(KeyApiKey);
        if (string.IsNullOrEmpty(apiKey))
        {
            _host.ShowThought("⚠️ 未配置", "请在设置 → 网页分析器中填写 API Key");
            return;
        }

        // ── 获取 URL ──
        var url = await _host.ShowInputDialog("🌐 输入网址", "https://", "https://");
        if (string.IsNullOrEmpty(url)) return;

        // 补全协议
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        try
        {
            // ── 抓取网页 ──
            _host.ShowThought("🌐 抓取中", $"正在获取: {url}");
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);

            var html = await client.GetStringAsync(url);

            // 提取纯文本（去 HTML 标签）
            var text = StripHtml(html);
            if (text.Length > 8000)
                text = text[..8000] + "\n\n[内容过长，已截取前 8000 字符]";

            // ── AI 分析 ──
            _host.ShowThought("🤖 分析中", "正在调用 AI 分析网页内容...");

            var report = await AnalyzeWithAi(apiKey, url, text);

            // ── 保存报告 ──
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var reportDir = Path.Combine(desktop, "WebAnalyzer");
            Directory.CreateDirectory(reportDir);

            var domain = new Uri(url).Host.Replace("www.", "");
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{domain}_{timestamp}.md";
            var filePath = Path.Combine(reportDir, fileName);

            await File.WriteAllTextAsync(filePath, report, Encoding.UTF8);

            // ── 询问是否打开 ──
            var open = await _host.ShowConfirmDialog("📄 报告已生成",
                $"已保存到:\n{fileName}\n\n是否用默认应用打开？");

            if (open)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                });
                _host.ShowReaction("📄");
            }
            else
            {
                _host.ShowThought("📄 已保存", $"报告已保存到桌面 WebAnalyzer/ 目录\n{fileName}");
            }
        }
        catch (HttpRequestException ex)
        {
            _host.ShowThought("❌ 抓取失败", $"无法访问网页:\n{ex.Message}");
        }
        catch (Exception ex)
        {
            _host.ShowThought("❌ 分析失败", ex.Message);
        }
    }

    /// <summary>调用 AI API 分析网页内容，返回 Markdown 报告</summary>
    private async Task<string> AnalyzeWithAi(string apiKey, string url, string content)
    {
        var apiUrl = _host?.GetConfig(KeyApiUrl) ?? "https://api.deepseek.com/v1/chat/completions";
        var model = _host?.GetConfig(KeyModel) ?? "deepseek-chat";

        var prompt = $"请分析以下网页内容，生成一份结构化 Markdown 报告。\n\n" +
                     "## 格式要求\n" +
                     "- 使用 Markdown 格式\n" +
                     "- 包含标题、列表、引用等元素\n\n" +
                     "## 内容要求\n" +
                     "### 概述\n" +
                     "用 1-2 句话概括页面的核心主题\n\n" +
                     "### 关键要点\n" +
                     "提取 3-8 个最重要的信息点，以列表呈现\n\n" +
                     "### 详细分析\n" +
                     "对重点内容展开说明\n\n" +
                     "### 总结\n" +
                     "简要结论或建议\n\n" +
                     $"## 原始网页\n" +
                     $"来源: {url}\n\n" +
                     $"## 网页内容\n" +
                     $"{content}";

        var body = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "你是一个专业的网页内容分析助手。请根据用户提供的网页内容，生成结构化的 Markdown 分析报告。使用中文回复。" },
                new { role = "user", content = prompt },
            },
            max_tokens = 4096,
            temperature = 0.3,
        };

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.Timeout = TimeSpan.FromSeconds(60);

        var jsonBody = JsonSerializer.Serialize(body);
        var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(apiUrl, httpContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        // 兼容 OpenAI / DeepSeek 返回格式
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var msg = choices[0].TryGetProperty("message", out var m)
                ? m : choices[0];

            var text = msg.TryGetProperty("content", out var c)
                ? c.GetString() ?? ""
                : msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

            // 包装成完整 Markdown 文档
            var domain = new Uri(url).Host;
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            return $"# 网页分析报告\n\n" +
                   $"**来源**: [{url}]({url})\n\n" +
                   $"**分析时间**: {date}\n\n" +
                   $"---\n\n" +
                   $"{text.Trim()}";
        }

        return "❌ AI 返回了异常格式，请检查 API 配置。";
    }

    /// <summary>去除 HTML 标签，提取纯文本</summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // 移除脚本和样式
        var text = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // 移除 HTML 标签
        text = Regex.Replace(text, @"<[^>]+>", " ");

        // 解码 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);

        // 合并空白
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\n\s*\n", "\n");

        return text.Trim();
    }
}
