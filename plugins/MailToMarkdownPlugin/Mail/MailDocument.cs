namespace MailToMarkdownPlugin.Mail;

internal sealed class MailAttachment
{
    public string FileName { get; set; } = "attachment";
    public string? ContentId { get; set; }
    public string MimeType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsInline { get; set; }
}

internal sealed class MailDocument
{
    public string? Subject { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string? DateRaw { get; set; }
    public string? PlainBody { get; set; }
    public string? HtmlBody { get; set; }
    public List<MailAttachment> Attachments { get; } = new();
}
