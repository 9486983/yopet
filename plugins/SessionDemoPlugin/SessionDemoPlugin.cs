using System.Diagnostics;
using yopet.Core.Models;
using yopet.Sdk;

namespace SessionDemoPlugin;

/// <summary>
/// 会话多步工作流示例插件。
///
/// 使用流程：
///   1. 拖入一个文件夹 → 会话启动，插件在该文件夹下创建 _output 子目录
///   2. 拖入 .jpg/.png 文件 → 自动复制到输出目录并重命名（IMG_0001 序列）
///   3. 实时显示进度和计数
///   4. 右键 → "⏹️ 结束图片整理" 结束会话
/// </summary>
[Plugin("图片整理器", Version = "1.0.0", Description = "会话工作流示例：拖入文件夹启动，再拖入图片自动整理")]
public class SessionDemoPlugin : PluginBase
{
    public override string Name => "图片整理器";

    public override async Task InitializeAsync(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = "图片整理",
            Emoji = "🖼️",
            Description = "拖入文件夹启动会话，再拖入 JPG/PNG 自动收集整理",
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.Both,
            FileCallback = async (paths) =>
            {
                try
                {
                    await OnFilesDropped(paths, host);
                }
                catch (Exception ex)
                {
                    host.ShowThought("❌ 错误", ex.Message);
                }
            },
        });

        await Task.CompletedTask;
    }

    /// <summary>核心处理逻辑 —— 区分首次拖入（启动会话）和后续拖入</summary>
    private static async Task OnFilesDropped(string[] paths, IPluginHost host)
    {
        var session = host.CurrentSession;

        // ── 已存在活跃会话：后续拖入，直接处理 ──
        if (session?.IsActive == true)
        {
            await HandleImages(paths, host, session);
            return;
        }

        // ── 首次拖入：校验 → 启动会话 ──
        var folder = paths[0];
        if (!Directory.Exists(folder))
        {
            host.ShowThought("❌ 需要文件夹", "请拖入一个文件夹来启动图片整理会话");
            return;
        }

        // 准备输出目录
        var outputDir = Path.Combine(folder, Path.GetFileName(folder) + "_整理输出");
        Directory.CreateDirectory(outputDir);

        // 启动会话（自动激活同名动作，后续拖入不再弹菜单）
        session = host.StartSession("图片整理");
        session.Context["outputDir"] = outputDir;
        session.Context["count"] = 0;

        session.Status = "已就绪，拖入图片开始整理";
        host.ShowThought("📋 会话已启动",
            $"输出目录：{outputDir}\n\n拖入 JPG/PNG 文件自动整理到此目录。");

        // 如果拖入的正好也是图片文件，一并处理
        if (paths.Any(p => IsImage(p)))
            await HandleImages(paths, host, session);
    }

    /// <summary>处理图片文件：复制到输出目录并序列化命名</summary>
    private static async Task HandleImages(string[] paths, IPluginHost host, ISession session)
    {
        var outputDir = (string)session.Context["outputDir"]!;
        var imagePaths = paths.Where(IsImage).ToArray();

        if (imagePaths.Length == 0)
        {
            host.ShowThought("ℹ️ 跳过", "没有检测到 JPG/PNG 图片文件");
            return;
        }

        // 用 RunWithAnimation 展示工作状态，支持取消
        await host.RunWithAnimation(PetAnimation.Running, async (token) =>
        {
            foreach (var img in imagePaths)
            {
                token.ThrowIfCancellationRequested();

                var count = (int)session.Context["count"]! + 1;
                var ext = Path.GetExtension(img);
                var newName = $"IMG_{count:D4}{ext}";
                var dest = Path.Combine(outputDir, newName);

                // 模拟一些处理时间（实际场景可换成图片压缩等耗时操作）
                await Task.Delay(300, token);
                File.Copy(img, dest, overwrite: false);

                // 更新会话状态
                session.Context["count"] = count;
                session.Status = $"已整理 {count} 张";
                session.Progress = count switch
                {
                    <= 5 => -1,           // 前 5 张不确定进度
                    _ => count / 50.0,     // 之后按 50 张总量估算
                };

                host.ShowThought($"🖼️ 已整理 {count} 张",
                    $"{Path.GetFileName(img)}\n→ {newName}");
            }
        });

        // 检查是否达到结束条件（示例：整理 10 张后自动完成）
        var total = (int)session.Context["count"]!;
        if (total >= 10)
        {
            session.Complete();
            host.ShowThought("✅ 整理完成",
                $"共整理 {total} 张图片到：\n{outputDir}");
        }
        else
        {
            host.ShowThought("📋 继续", $"已整理 {total}/10 张，再拖入一些吧");
        }
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png";
    }
}
