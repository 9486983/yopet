using System.Text;

namespace MailToMarkdownPlugin.Mail;

internal sealed class MailConversionOptions
{
    public required string SourcePath { get; init; }
    public required string OutputDirectory { get; init; }
    public bool ExportAttachments { get; init; } = true;
    public required MarkdownLabels Labels { get; init; }
}

internal static class MailConverter
{
    public static string Convert(MailConversionOptions options)
    {
        var sourcePath = options.SourcePath;
        var document = Path.GetExtension(sourcePath).Equals(".msg", StringComparison.OrdinalIgnoreCase)
            ? MsgParser.Parse(sourcePath)
            : EmlParser.Parse(sourcePath);

        var outputDirectory = options.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        var fallbackSubject = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath), "mail");
        var id = Guid.NewGuid().ToString();
        var markdownPath = Path.Combine(outputDirectory, id + ".md");
        var attachmentDirectoryName = id + ".assets";
        var attachmentDirectory = Path.Combine(outputDirectory, attachmentDirectoryName);
        var needsAttachmentDirectory = options.ExportAttachments && document.Attachments.Count > 0;

        var links = new string?[document.Attachments.Count];
        if (needsAttachmentDirectory)
        {
            Directory.CreateDirectory(attachmentDirectory);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < document.Attachments.Count; i++)
            {
                var attachment = document.Attachments[i];
                var fileName = MakeUniqueName(
                    SanitizeFileName(attachment.FileName, $"attachment_{i + 1}"), usedNames);
                File.WriteAllBytes(Path.Combine(attachmentDirectory, fileName), attachment.Data);
                links[i] = EncodeRelativePath(attachmentDirectoryName + "/" + fileName);
            }
        }

        var markdown = MarkdownRenderer.Render(document, fallbackSubject, links, options.Labels);
        File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
        return markdownPath;
    }

    private static string SanitizeFileName(string? name, string fallback)
    {
        var fileName = Path.GetFileName(name ?? "");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = fallback;

        var builder = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) || "<>:\"/\\|?*".Contains(c) || char.IsControl(c)
                ? '_'
                : c);
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length == 0) result = fallback;
        if (result.Length <= 120) return result;

        var extension = Path.GetExtension(result);
        var stemLength = Math.Max(1, 120 - extension.Length);
        return result[..Math.Min(result.Length, stemLength)] + extension;
    }

    private static string MakeUniqueName(string name, HashSet<string> usedNames)
    {
        if (usedNames.Add(name)) return name;

        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var index = 1;

        while (true)
        {
            var candidate = $"{stem}_{index}{extension}";
            if (usedNames.Add(candidate)) return candidate;
            index++;
        }
    }

    private static string EncodeRelativePath(string relativePath) =>
        string.Join("/", relativePath.Split('/').Select(part =>
            Uri.EscapeDataString(part).Replace("(", "%28").Replace(")", "%29")));
}
