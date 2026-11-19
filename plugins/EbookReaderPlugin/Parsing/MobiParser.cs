using System.Text;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>
/// MOBI / AZW / PRC 解析器。
/// 支持未压缩与 PalmDOC LZ77 压缩；带 DRM 或 HUFF/CDIC 压缩的文件会给出明确提示。
/// AZW3 (KF8) 若包含 MOBI7 回退正文，也可以正常阅读。
/// </summary>
public static class MobiParser
{
    private const int PalmDocCompression = 2;
    private const int HuffCdicCompression = 17480;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    static MobiParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static EbookDocument Parse(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 86)
            throw new InvalidDataException("文件太小，不是有效的 MOBI/AZW 文件");

        var recordCount = ReadUInt16(data, 76);
        if (recordCount <= 1)
            throw new InvalidDataException("MOBI/AZW 文件记录数量异常");

        var offsets = new int[recordCount + 1];
        for (var i = 0; i < recordCount; i++)
        {
            var offset = ReadInt32(data, 78 + i * 8);
            if (offset < 0 || offset >= data.Length)
                throw new InvalidDataException("MOBI/AZW 文件记录偏移损坏");
            offsets[i] = offset;
        }

        offsets[recordCount] = data.Length;
        for (var i = 1; i <= recordCount; i++)
        {
            if (offsets[i] < offsets[i - 1])
                throw new InvalidDataException("MOBI/AZW 文件记录顺序损坏");
        }

        var record0 = offsets[0];
        var record0End = offsets[1];
        if (record0End - record0 < 16)
            throw new InvalidDataException("MOBI/AZW 文件头损坏");

        var compression = ReadUInt16(data, record0);
        var textLength = ReadUInt32(data, record0 + 4);
        var textRecordCount = ReadUInt16(data, record0 + 8);
        var encryption = ReadUInt16(data, record0 + 12);

        if (encryption != 0)
            throw new NotSupportedException("该电子书带有 DRM 保护，无法打开");

        if (textRecordCount == 0 || textRecordCount + 1 > recordCount)
            throw new InvalidDataException("MOBI/AZW 文本记录数量异常");

        var title = TextUtil.FileTitle(path);
        var author = "";
        var textEncoding = 1252;
        uint extraDataFlags = 0;
        var firstTextRecord = 1;
        var textEndRecord = Math.Min(recordCount, textRecordCount + 1);

        var hasMobiHeader = record0End - record0 >= 20 &&
                            ReadAscii(data, record0 + 16, 4) == "MOBI";

        if (hasMobiHeader)
        {
            var mobi = record0 + 16;
            var mobiLength = (int)Math.Min(ReadUInt32(data, mobi + 4), (uint)(record0End - mobi));
            if (mobiLength < 0x18)
                throw new InvalidDataException("MOBI 头长度异常");

            textEncoding = (int)ReadUInt32(data, mobi + 0x0C);

            var firstNonBook = (int)ReadUInt32(data, mobi + 0x50);
            if (firstNonBook > 0 && firstNonBook <= recordCount)
                textEndRecord = Math.Min(textEndRecord, firstNonBook);

            var fullNameOffset = (int)ReadUInt32(data, mobi + 0x54);
            var fullNameLength = (int)ReadUInt32(data, mobi + 0x58);
            if (fullNameOffset > 0 &&
                fullNameLength > 0 &&
                fullNameOffset + fullNameLength <= record0End - record0)
            {
                var name = Decode(data, record0 + fullNameOffset, fullNameLength, textEncoding).Trim('\0', ' ', '\n', '\r');
                if (name.Length > 0)
                    title = name;
            }

            if (record0End - mobi >= 0xC2)
            {
                var contentRecord = ReadUInt16(data, mobi + 0xC0);
                if (contentRecord > 0 && contentRecord < recordCount)
                    firstTextRecord = contentRecord;
            }

            if (mobiLength >= 0xF4 && record0End - mobi >= 0xF4)
                extraDataFlags = ReadUInt32(data, mobi + 0xF0);

            var exthFlags = mobiLength >= 0x84 && record0End - mobi >= 0x84
                ? ReadUInt32(data, mobi + 0x80)
                : 0;

            if ((exthFlags & 0x40) != 0)
            {
                ParseExth(data, mobi + mobiLength, record0End, textEncoding, out author, out var exthTitle);
                if (!string.IsNullOrWhiteSpace(exthTitle))
                    title = exthTitle;
            }
        }

        if (firstTextRecord < 1 || firstTextRecord >= textEndRecord)
            throw new InvalidDataException("MOBI/AZW 文本区为空");

        using var output = new MemoryStream();
        for (var i = firstTextRecord; i < textEndRecord; i++)
        {
            var raw = data.AsSpan(offsets[i], offsets[i + 1] - offsets[i]).ToArray();
            var segment = extraDataFlags != 0 ? StripTrailingEntries(raw, extraDataFlags) : raw;

            switch (compression)
            {
                case 1:
                    output.Write(segment);
                    break;

                case PalmDocCompression:
                    DecompressPalmDoc(segment, output);
                    break;

                case HuffCdicCompression:
                    throw new NotSupportedException("该 MOBI/AZW3 使用 HUFF/CDIC 压缩，暂不支持，请转换为 EPUB 或 TXT");

                default:
                    throw new NotSupportedException($"不支持的 MOBI 压缩类型：{compression}");
            }
        }

        var textBytes = output.ToArray();
        if (textLength > 0 && textLength < textBytes.Length)
            textBytes = textBytes[..(int)textLength];

        var html = Decode(textBytes, 0, textBytes.Length, textEncoding);
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidDataException("MOBI/AZW 中没有可阅读的文本");

        var (htmlTitle, chapters) = HtmlTextConverter.BuildFromHtml(html, title);
        if (string.IsNullOrWhiteSpace(title) || title == TextUtil.FileTitle(path))
            title = htmlTitle;

        if (chapters.Count == 0)
        {
            var plain = HtmlTextConverter.ToPlainText(html);
            if (plain.Length == 0)
                throw new InvalidDataException("MOBI/AZW 中没有可阅读的文本");
            chapters.Add(new Chapter { Title = title, Text = plain });
        }

        var document = new EbookDocument { Title = title, Author = author };
        document.Chapters.AddRange(chapters);
        return document;
    }

    private static void ParseExth(
        byte[] data,
        int position,
        int end,
        int encoding,
        out string author,
        out string title)
    {
        author = "";
        title = "";
        if (position + 12 > end || ReadAscii(data, position, 4) != "EXTH")
            return;

        var count = ReadInt32(data, position + 8);
        var cursor = position + 12;

        for (var i = 0; i < count && cursor + 8 <= end; i++)
        {
            var type = ReadInt32(data, cursor);
            var length = ReadInt32(data, cursor + 4);
            if (length < 8 || cursor + length > end)
                break;

            if (type == 100 && author.Length == 0)
                author = TextUtil.CleanInline(Decode(data, cursor + 8, length - 8, encoding));
            else if (type == 503 && title.Length == 0)
                title = TextUtil.CleanInline(Decode(data, cursor + 8, length - 8, encoding));

            cursor += length;
        }
    }

    private static byte[] StripTrailingEntries(byte[] data, uint flags)
    {
        var end = data.Length;

        for (var bit = 15; bit >= 1; bit--)
        {
            if ((flags & (1u << bit)) == 0)
                continue;

            var size = 0;
            var cursor = end - 1;
            while (cursor >= 0)
            {
                var value = data[cursor--];
                size = (size << 7) | (value & 0x7F);
                if ((value & 0x80) != 0)
                    break;
            }

            if (size <= 0 || size > end)
                return data;

            end -= size;
        }

        if ((flags & 0x1) != 0 && end > 0)
        {
            var count = data[end - 1] & 0x03;
            end -= Math.Min(count + 1, end);
        }

        return end <= 0 ? Array.Empty<byte>() : data[..end];
    }

    private static void DecompressPalmDoc(byte[] input, Stream output)
    {
        var result = new List<byte>(4096);
        var index = 0;

        while (index < input.Length)
        {
            var value = input[index++];

            if (value == 0)
            {
                result.Add(0);
            }
            else if (value <= 8)
            {
                for (var i = 0; i < value && index < input.Length; i++)
                    result.Add(input[index++]);
            }
            else if (value <= 0x7F)
            {
                result.Add(value);
            }
            else if (value <= 0xBF)
            {
                if (index >= input.Length)
                    break;

                var pair = (value << 8) | input[index++];
                var distance = (pair >> 3) & 0x7FF;
                var length = (pair & 0x7) + 3;
                if (distance <= 0)
                    continue;

                var sourceIndex = result.Count - distance;
                for (var i = 0; i < length; i++)
                {
                    if (sourceIndex + i >= 0 && sourceIndex + i < result.Count)
                        result.Add(result[sourceIndex + i]);
                    else
                        result.Add(0);
                }
            }
            else
            {
                result.Add(0x20);
                result.Add((byte)(value ^ 0x80));
            }
        }

        output.Write(result.ToArray());
    }

    private static string Decode(byte[] data, int offset, int length, int encoding)
    {
        if (length <= 0 || offset < 0 || offset >= data.Length)
            return "";

        length = Math.Min(length, data.Length - offset);
        var value = encoding switch
        {
            65001 => Utf8,
            1252 => Encoding.GetEncoding(1252),
            936 => Encoding.GetEncoding(936),
            _ => TryGetEncoding(encoding),
        };

        return value.GetString(data, offset, length);
    }

    private static Encoding TryGetEncoding(int encoding)
    {
        try
        {
            return Encoding.GetEncoding(encoding);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static int ReadInt32(byte[] data, int offset) => unchecked((int)ReadUInt32(data, offset));

    private static string ReadAscii(byte[] data, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > data.Length)
            return "";

        return Encoding.ASCII.GetString(data, offset, length);
    }
}
