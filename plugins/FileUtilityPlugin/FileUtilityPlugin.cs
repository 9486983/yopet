using System.IO.Compression;
using System.Security.Cryptography;
using yopet.Core.Models;
using yopet.Sdk;

namespace FileUtilityPlugin;

[Plugin("文件工具", Version = "2.1.0", Description = "文件/文件夹详情、哈希、压缩、打开等")]
public class FileUtilityPlugin : PluginBase
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml",
        ".ini", ".cfg", ".conf", ".log", ".bat", ".cmd", ".ps1",
        ".sh", ".js", ".ts", ".jsx", ".tsx", ".css", ".html",
        ".cs", ".cpp", ".c", ".h", ".hpp", ".java", ".rs", ".go",
        ".rb", ".php", ".sql", ".r", ".swift", ".kt", ".dart",
    };

    public override string Name => "文件工具";

    public override async Task InitializeAsync(IPluginHost host)
    {
        RegisterViewDetails(host);
        RegisterOpenLocation(host);
        RegisterCopyPath(host);
        RegisterMd5(host);
        RegisterZip(host);
        RegisterNotepad(host);
        RegisterSleepTest(host);
        await Task.CompletedTask;
    }

    // ── 查看详情 ──
    private static void RegisterViewDetails(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "查看详情", Emoji = "🔍",
            Description = "自动识别：文件夹/文件/文本预览",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.Both, CanActivate = true,
            FileCallback = async (paths) =>
            {
                try
                {
                    var path = paths[0];
                    if (Directory.Exists(path))
                    {
                        var dir = new DirectoryInfo(path);
                        host.ShowThought("📁 文件夹详情",
                            $"名称: {dir.Name}\n位置: {dir.FullName}\n" +
                            $"子文件夹: {dir.GetDirectories().Length} 个\n" +
                            $"文件: {dir.GetFiles().Length} 个\n" +
                            $"总大小: {FormatSize(dir.GetFiles().Sum(f => f.Length))}");
                        host.ShowReaction("📁"); return;
                    }

                    var fi = new FileInfo(path);
                    if (!fi.Exists) { host.ShowThought("❌ 错误", "文件不存在"); return; }

                    if (TextExtensions.Contains(fi.Extension.ToLowerInvariant()))
                    {
                        var text = await File.ReadAllTextAsync(path);
                        var preview = text.Length > 500 ? text[..500] + "\n\n…（仅显示前 500 字符）" : text;
                        host.ShowThought("📄 文本预览",
                            $"文件: {fi.Name}  大小: {FormatSize(fi.Length)}\n\n─── 内容预览 ───\n{preview}");
                    }
                    else
                    {
                        host.ShowThought("📄 文件详情",
                            $"名称: {fi.Name}\n大小: {FormatSize(fi.Length)}\n" +
                            $"位置: {fi.DirectoryName}\n修改: {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                    }
                    host.ShowReaction("🔍");
                }
                catch (Exception ex) { host.ShowThought("❌ 错误", ex.Message); }
            },
        });
    }

    // ── 打开位置 ──
    private static void RegisterOpenLocation(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "打开位置", Emoji = "📂",
            Description = "在资源管理器中定位文件",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.Both, CanActivate = true,
            FileCallback = async (paths) =>
            {
                if (paths.Length == 0) return;
                var dir = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{paths[0]}\"");
                    host.ShowReaction("📂");
                }
            },
        });
    }

    // ── 复制路径 ──
    private static void RegisterCopyPath(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "复制路径", Emoji = "📋",
            Description = "复制完整路径到剪贴板",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.Both, CanActivate = true,
            FileCallback = async (paths) =>
            {
                if (paths.Length == 0) return;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"Set-Clipboard -Value '{paths[0].Replace("'", "''")}'\"",
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    System.Diagnostics.Process.Start(psi);
                    host.ShowReaction("📋");
                }
                catch { }
            },
        });
    }

    // ── MD5 ──
    private static void RegisterMd5(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "计算 MD5", Emoji = "🔐",
            Description = "计算文件 MD5 哈希值",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.File, CanActivate = true,
            FileCallback = async (paths) =>
            {
                if (paths.Length == 0) return;
                try
                {
                    await host.RunWithAnimation(PetAnimation.Running, async (token) =>
                    {
                        using var stream = File.OpenRead(paths[0]);
                        var hash = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
                        host.ShowThought("🔐 MD5 哈希", $"{Path.GetFileName(paths[0])}\n\n{hash}");
                    });
                    host.ShowReaction("🔐");
                }
                catch (Exception ex) { host.ShowThought("❌ 错误", ex.Message); }
            },
        });
    }

    // ── ZIP ──
    private static void RegisterZip(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "压缩为 ZIP", Emoji = "📦",
            Description = "将文件/文件夹压缩为 ZIP",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.Both, CanActivate = true,
            FileCallback = async (paths) =>
            {
                if (paths.Length == 0) return;
                try
                {
                    await host.RunWithAnimation(PetAnimation.Running, async (token) =>
                    {
                        var path = paths[0];
                        var isDir = Directory.Exists(path);
                        var zipPath = path + ".zip";
                        if (File.Exists(zipPath))
                        {
                            var d = Path.GetDirectoryName(path)!;
                            var n = Path.GetFileNameWithoutExtension(path);
                            zipPath = Path.Combine(d, $"{n}_1.zip");
                        }
                        if (isDir) ZipFile.CreateFromDirectory(path, zipPath);
                        else
                        {
                            using var zs = File.OpenWrite(zipPath);
                            using var z = new ZipArchive(zs, ZipArchiveMode.Create);
                            var e = z.CreateEntry(Path.GetFileName(path));
                            using var es = e.Open();
                            using var fs = File.OpenRead(path);
                            fs.CopyTo(es);
                        }
                        host.ShowThought("📦 压缩完成",
                            $"已创建: {Path.GetFileName(zipPath)}\n" +
                            $"大小: {FormatSize(new FileInfo(zipPath).Length)}");
                    });
                    host.ShowReaction("📦");
                }
                catch (Exception ex) { host.ShowThought("❌ 错误", ex.Message); }
            },
        });
    }

    // ── 记事本 ──
    private static void RegisterNotepad(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "记事本打开", Emoji = "🖥️",
            Description = "用记事本打开文件（文本类）",
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.File, CanActivate = true,
            FileCallback = async (paths) =>
            {
                if (paths.Length > 0)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("notepad.exe", paths[0]);
                        host.ShowReaction("🖥️");
                    }
                    catch (Exception ex) { host.ShowThought("❌ 错误", ex.Message); }
                }
            },
        });
    }

    // ── 睡眠测试（演示持续动画） ──
    private static void RegisterSleepTest(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "睡眠测试 10s",
            Emoji = "💤",
            Description = "多动画轮换: 思考/开心交替 10 秒，可取消",
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.Both,
            FileCallback = async (paths) =>
            {
                try
                {
                    await host.RunWithAnimation(
                        new[] { PetAnimation.Running, PetAnimation.Wave, PetAnimation.Jump },
                        async (token) =>
                        {
                            for (var i = 10; i > 0; i--)
                            {
                                token.ThrowIfCancellationRequested();
                                host.ShowThought("💤 睡眠测试",
                                    $"剩余 {i} 秒…（点击右上角进度环取消）");
                                await Task.Delay(1000, token);
                            }
                        });
                    host.ShowThought("✅ 测试完成", "睡眠结束，动画已自动恢复待机。");
                }
                catch (OperationCanceledException)
                {
                    host.ShowThought("⏹️ 已取消", "任务已被用户取消，动画已恢复。");
                }
            },
        });
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
