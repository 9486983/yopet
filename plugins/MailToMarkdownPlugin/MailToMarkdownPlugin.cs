using System.Text;
using Lang.Avalonia;
using MailToMarkdownPlugin.Mail;
using yopet.Core.Models;
using yopet.Sdk;

namespace MailToMarkdownPlugin;

[Plugin("邮件转 Markdown", Version = "1.0.0", Description = "将 EML / MSG 邮件转换为 Markdown 文档")]
public class MailToMarkdownPlugin : PluginBase
{
    private static readonly string[] MailExtensions = { ".eml", ".msg" };

    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.MailToMarkdownPlugin.{key}");

    public override string Name => T("Name");

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);

        host.RegisterAction(new PluginAction
        {
            Name = T("ActionConvert"),
            Emoji = "📝",
            Description = T("ActionConvertDescription"),
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.File,
            FileExtensions = MailExtensions,
            CanActivate = true,
            FileCallback = paths => ConvertAsync(paths, host),
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("ConfigName"),
            Emoji = "⚙️",
            Group = T("Group"),
            Description = T("ConfigDescription"),
            Display = LocalizedDisplay.Of(
                name: () => T("ConfigName"),
                group: () => T("Group"),
                description: () => T("ConfigDescription")),
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog("mail2md");
                return Task.CompletedTask;
            },
        });

        host.RegisterConfig(new PluginConfigSection
        {
            Key = "mail2md",
            Title = T("ConfigTitle"),
            Emoji = "⚙️",
            Fields = new()
            {
                new()
                {
                    Key = "output_dir",
                    Label = T("OutputDirLabel"),
                    Type = PluginConfigFieldType.FolderPath,
                    Placeholder = T("OutputDirPlaceholder"),
                    Description = T("OutputDirDesc"),
                },
                new()
                {
                    Key = "export_attachments",
                    Label = T("ExportAttachmentsLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "true",
                    Description = T("ExportAttachmentsDesc"),
                },
            },
        });
    }

    private static async Task ConvertAsync(string[] paths, IPluginHost host)
    {
        var files = paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsMailFile(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0) return;

        var labels = CreateLabels();
        var configuredOutputDirectory = host.GetConfig("output_dir");
        var exportAttachments = !string.Equals(
            host.GetConfig("export_attachments"), "false", StringComparison.OrdinalIgnoreCase);

        try
        {
            await host.RunWithAnimation(PetAnimation.Running, async token =>
            {
                var (outputs, errors) = await Task.Run(() =>
                {
                    var converted = new List<string>();
                    var failures = new List<string>();

                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            converted.Add(ConvertFile(
                                file, configuredOutputDirectory, exportAttachments, labels));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        }
                    }

                    return (Outputs: converted, Errors: failures);
                }, token);

                host.ShowThought(
                    errors.Count > 0 ? T("FailedTitle") : T("DoneTitle"),
                    BuildSummary(outputs, errors));
            });
        }
        catch (OperationCanceledException)
        {
            host.ShowThought(T("CanceledTitle"), T("CanceledMessage"));
        }
        catch (Exception ex)
        {
            host.ShowThought(T("FailedTitle"), ex.Message);
        }
    }

    private static MarkdownLabels CreateLabels() => new()
    {
        From = T("MetaFrom"),
        To = T("MetaTo"),
        Cc = T("MetaCc"),
        Date = T("MetaDate"),
        EmptyBody = T("EmptyBody"),
        Attachments = T("AttachmentsHeading"),
    };

    private static string ConvertFile(
        string sourcePath,
        string? configuredOutputDirectory,
        bool exportAttachments,
        MarkdownLabels labels)
    {
        return MailConverter.Convert(new MailConversionOptions
        {
            SourcePath = sourcePath,
            OutputDirectory = ResolveOutputDirectory(sourcePath, configuredOutputDirectory),
            ExportAttachments = exportAttachments,
            Labels = labels,
        });
    }

    private static string ResolveOutputDirectory(string sourcePath, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var fullPath = Path.GetFullPath(configured.Trim());
                if (!File.Exists(fullPath)) return fullPath;
            }
            catch
            {
            }
        }

        return Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
    }

    private static string BuildSummary(IReadOnlyList<string> outputs, IReadOnlyList<string> errors)
    {
        var builder = new StringBuilder();

        if (outputs.Count == 1 && errors.Count == 0)
        {
            builder.Append(string.Format(T("DoneSingle"), outputs[0]));
        }
        else if (outputs.Count > 0)
        {
            var names = string.Join("\n", outputs.Select(Path.GetFileName));
            builder.Append(string.Format(T("DoneMulti"), outputs.Count, names));
        }

        if (errors.Count > 0)
        {
            if (builder.Length > 0) builder.Append("\n\n");
            builder.Append(string.Join("\n", errors));
        }

        return builder.ToString();
    }

    private static bool IsMailFile(string path) =>
        MailExtensions.Any(extension =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
