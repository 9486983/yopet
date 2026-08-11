using System.IO.Compression;
using System.Security.Cryptography;
using yopet.Core.Models;
using yopet.Sdk;
using Lang.Avalonia;

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

    private static string T(string key) => I18nManager.Instance.GetResource($"Localization.FileUtilityPlugin.{key}");

    public override string Name => T("Name");

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
            Name = T("ActionViewDetails"), Emoji = "🔍",
            Description = T("ActionViewDetailsDescription"),
            Target = ActionTarget.RadialMenu, AcceptType = ItemType.Both, CanActivate = true,
            FileCallback = async (paths) =>
            {
                try
                {
                    var path = paths[0];
                    if (Directory.Exists(path))
                    {
                        var dir = new DirectoryInfo(path);
                        host.ShowThought(T("FolderDetailsTitle"),
                            string.Format(T("FolderDetailsText1"), dir.Name, dir.FullName) +
                            string.Format(T("FolderSubCount"), dir.GetDirectories().Length) +
                            string.Format(T("FolderFileCount"), dir.GetFiles().Length) +
                            string.Format(T("TotalSize"), FormatSize(dir.GetFiles().Sum(f => f.Length))));
                        host.ShowReaction("📁"); return;
                    }

                    var fi = new FileInfo(path);
                    if (!fi.Exists) { host.ShowThought(T("ErrorTitle"), T("FileNotExists")); return; }

                    if (TextExtensions.Contains(fi.Extension.ToLowerInvariant()))
                    {
                        var text = await File.ReadAllTextAsync(path);
                        var preview = text.Length > 500 ? text[..500] + "\n\n" + T("PreviewTruncated") : text;
                        host.ShowThought(T("TextPreviewTitle"),
                            string.Format(T("TextPreviewText"), fi.Name, FormatSize(fi.Length), preview));
                    }
                    else
                    {
                        host.ShowThought(T("FileDetailsTitle"),
                            string.Format(T("FileDetailsText1"), fi.Name, FormatSize(fi.Length)) +
                            string.Format(T("FileDetailsText2"), fi.DirectoryName, fi.LastWriteTime));
                    }
                    host.ShowReaction("🔍");
                }
                catch (Exception ex) { host.ShowThought(T("ErrorTitle"), ex.Message); }
            },
        });
    }

    // ── 打开位置 ──
    private static void RegisterOpenLocation(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = T("ActionOpenLocation"), Emoji = "📂",
            Description = T("ActionOpenLocationDescription"),
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
            Name = T("ActionCopyPath"), Emoji = "📋",
            Description = T("ActionCopyPathDescription"),
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
            Name = T("ActionMd5"), Emoji = "🔐",
            Description = T("ActionMd5Description"),
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
                        host.ShowThought(T("Md5Title"), $"{Path.GetFileName(paths[0])}\n\n{hash}");
                    });
                    host.ShowReaction("🔐");
                }
                catch (Exception ex) { host.ShowThought(T("ErrorTitle"), ex.Message); }
            },
        });
    }

    // ── ZIP ──
    private static void RegisterZip(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = T("ActionZip"), Emoji = "📦",
            Description = T("ActionZipDescription"),
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
                        host.ShowThought(T("ZipDoneTitle"),
                            string.Format(T("ZipCreatedText"), Path.GetFileName(zipPath)) +
                            string.Format(T("ZipSizeText"), FormatSize(new FileInfo(zipPath).Length)));
                    });
                    host.ShowReaction("📦");
                }
                catch (Exception ex) { host.ShowThought(T("ErrorTitle"), ex.Message); }
            },
        });
    }

    // ── 记事本 ──
    private static void RegisterNotepad(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = T("ActionNotepad"), Emoji = "🖥️",
            Description = T("ActionNotepadDescription"),
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
                    catch (Exception ex) { host.ShowThought(T("ErrorTitle"), ex.Message); }
                }
            },
        });
    }

    // ── 睡眠测试（演示持续动画） ──
    private static void RegisterSleepTest(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = T("ActionSleepTest"),
            Emoji = "💤",
            Description = T("ActionSleepTestDescription"),
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
                                host.ShowThought(T("SleepTestTitle"),
                                    string.Format(T("SleepTestRemaining"), i));
                                await Task.Delay(1000, token);
                            }
                        });
                    host.ShowThought(T("TestDoneTitle"), T("TestDoneMsg"));
                }
                catch (OperationCanceledException)
                {
                    host.ShowThought(T("CanceledTitle"), T("CanceledMsg"));
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
