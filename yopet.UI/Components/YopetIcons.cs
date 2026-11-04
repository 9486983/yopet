using System.Text.RegularExpressions;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using TKCode.Arc.Icons.Lucide;

namespace yopet.UI.Components;

public static class YopetIcons
{
    private static readonly Regex LeadingIcon = new(
        @"^\s*(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2600-\u27BF]|[\u2B00-\u2BFF]|\uFE0F|\u200D)+",
        RegexOptions.Compiled);

    public static string DisplayText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : LeadingIcon.Replace(text, "").Trim();

    public static LucideIconKind Resolve(string? emoji, LucideIconKind fallback = LucideIconKind.Puzzle)
    {
        if (string.IsNullOrWhiteSpace(emoji))
            return fallback;

        return FirstCodePoint(emoji.TrimStart()) switch
        {
            0x2699 => LucideIconKind.Settings,
            0x1F4C1 => LucideIconKind.Folder,
            0x1F4C2 => LucideIconKind.FolderOpen,
            0x1F5C2 => LucideIconKind.Archive,
            0x1F5C4 => LucideIconKind.Archive,
            0x1F5D1 => LucideIconKind.Trash2,
            0x1F4E6 => LucideIconKind.Package,
            0x1F4CB => LucideIconKind.Clipboard,
            0x1F4C4 => LucideIconKind.FileText,
            0x1F4DD => LucideIconKind.NotebookPen,
            0x1F50D => LucideIconKind.Search,
            0x1F510 => LucideIconKind.LockKeyhole,
            0x1F512 => LucideIconKind.LockKeyhole,
            0x1F513 => LucideIconKind.LockOpen,
            0x1F4CC => LucideIconKind.Pin,
            0x1F4B0 => LucideIconKind.CircleDollarSign,
            0x1F411 => LucideIconKind.Bot,
            0x1F47E => LucideIconKind.Bot,
            0x1F9BE => LucideIconKind.Bot,
            0x1F9E0 => LucideIconKind.Brain,
            0x1F300 => LucideIconKind.RefreshCw,
            0x1F9F9 => LucideIconKind.Eraser,
            0x1F4A4 => LucideIconKind.Moon,
            0x1F4A1 => LucideIconKind.Lightbulb,
            0x1F4AC => LucideIconKind.MessageCircle,
            0x1F4A5 => LucideIconKind.Zap,
            0x1F3AF => LucideIconKind.Target,
            0x1F680 => LucideIconKind.Rocket,
            0x1F3D1 => LucideIconKind.CalendarClock,
            0x1F7D1 => LucideIconKind.Palette,
            0x2795 => LucideIconKind.Plus,
            0x1F504 => LucideIconKind.RefreshCw,
            0x1F40D => LucideIconKind.Terminal,
            0x1F41D => LucideIconKind.Bug,
            0x26A1 => LucideIconKind.Zap,
            0x25B6 => LucideIconKind.Play,
            0x23F0 => LucideIconKind.AlarmClock,
            0x2705 => LucideIconKind.CircleCheck,
            0x274C => LucideIconKind.CircleX,
            0x23F9 => LucideIconKind.CircleStop,
            0x1F9EA => LucideIconKind.FlaskConical,
            0x1F3A8 => LucideIconKind.Palette,
            0x1F4E5 => LucideIconKind.Inbox,
            0x1F43E => LucideIconKind.PawPrint,
            0x2728 => LucideIconKind.Sparkles,
            _ => fallback,
        };
    }

    public static LucideIcon Create(
        string? emoji,
        double size = 16,
        LucideIconKind fallback = LucideIconKind.Puzzle) =>
        Create(Resolve(emoji, fallback), size);

    public static LucideIcon Create(LucideIconKind kind, double size = 16)
    {
        var icon = new LucideIcon
        {
            Kind = kind,
            Size = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ArcTokens.Bind(icon, ArcGlyph.ForegroundProperty, ArcDesignTokenKind.Foreground);
        return icon;
    }

    public static Geometry Geometry(
        string? emoji,
        LucideIconKind fallback = LucideIconKind.Puzzle) =>
        Lucide.Geometry(Resolve(emoji, fallback));

    private static int FirstCodePoint(string text)
    {
        if (text.Length == 0)
            return -1;

        if (char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]))
            return char.ConvertToUtf32(text[0], text[1]);

        return text[0];
    }
}
