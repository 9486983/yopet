using yopet.Core.Models;
using yopet.Sdk;
using EbookReaderPlugin.Core;
using EbookReaderPlugin.Views;

namespace EbookReaderPlugin;

[Plugin("电子书阅读器", Version = "1.0.0",
    Description = "在桌面宠物中阅读 EPUB / TXT / MOBI / AZW3 / FB2 电子书")]
public sealed class EbookReaderPlugin : PluginBase
{
    private readonly EbookService _service = new();

    public EbookReaderPlugin(PluginContext context) : base(context)
    {
    }

    public override string Name => T("Name", "电子书阅读器");

    public override string Description =>
        T("Description", "在桌面宠物中阅读 EPUB / TXT / MOBI / AZW3 / FB2 电子书");

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);

        host.RegisterAction(new PluginAction
        {
            Name = T("ShelfName", "书架"),
            Emoji = "📚",
            Group = T("Group", "电子书"),
            Description = T("ShelfDescription", "打开电子书书架"),
            Display = LocalizedDisplay.Of(
                name: () => T("ShelfName", "书架"),
                group: () => T("Group", "电子书"),
                description: () => T("ShelfDescription", "打开电子书书架")),
            Target = ActionTarget.ContextMenu,
            Callback = () => OpenLibraryAsync(host),
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("ImportName", "导入并阅读"),
            Emoji = "📖",
            Group = T("Group", "电子书"),
            Description = T("ImportDescription", "导入拖入的电子书并打开阅读器"),
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.File,
            FileExtensions = EbookService.SupportedExtensions,
            CanActivate = true,
            FileCallback = paths => ImportAndReadAsync(paths, host),
        });
    }

    private string T(string key, string fallback)
    {
        try
        {
            var value = Localizer.Get(key);
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private async Task OpenLibraryAsync(IPluginHost host)
    {
        var panel = new EbookPanel(host, _service, localize: key => T(key, ""));
        await host.ShowCustomViewAsync(panel);
    }

    private async Task ImportAndReadAsync(string[] paths, IPluginHost host)
    {
        var files = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return;

        ImportResult? result = null;

        try
        {
            await host.RunWithAnimation(PetAnimation.Review, async token =>
            {
                result = await _service.ImportAsync(files, token);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            host.ShowThought("📚 " + T("ImportFailedTitle", "电子书导入失败"), ex.Message);
            return;
        }

        if (result is null)
            return;

        if (result.Errors.Count > 0)
            host.ShowThought(
                "📚 " + T("ImportPartialTitle", "电子书导入完成（部分失败）"),
                string.Join("\n", result.Errors));
        else
            host.ShowReaction("📚", PetAnimation.Jump);

        var autoOpen = result.Errors.Count == 0 &&
                       result.Imported.Count + result.Skipped.Count == 1
            ? result.Imported.FirstOrDefault() ?? result.Skipped.FirstOrDefault()
            : null;

        var panel = new EbookPanel(host, _service, autoOpen, key => T(key, ""));
        await host.ShowCustomViewAsync(panel);
    }
}
