using SkiaSharp;

namespace EbookReaderPlugin.Core;

/// <summary>封面缩略图与提取出的主色。</summary>
public sealed record CoverThumbnail(byte[] Png, string ColorHex);

/// <summary>
/// 用 SkiaSharp 统一处理封面：缩放成小尺寸缩略图并提取主色。
/// 书架只保留缩略图，避免每个封面占用的内存随原图分辨率增长。
/// </summary>
public static class CoverImageProcessor
{
    private const int MaxWidth = 120;
    private const int MaxHeight = 170;

    private sealed class ColorBin
    {
        public double Weight;
        public double R;
        public double G;
        public double B;
    }

    public static CoverThumbnail? Create(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;

        try
        {
            using var bitmap = SKBitmap.Decode(data);
            return Create(bitmap, computeColor: true);
        }
        catch
        {
            return null;
        }
    }

    public static CoverThumbnail? CreateFromFile(string path, bool computeColor = true)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var bitmap = SKBitmap.Decode(path);
            return Create(bitmap, computeColor);
        }
        catch
        {
            return null;
        }
    }

    private static CoverThumbnail? Create(SKBitmap? original, bool computeColor)
    {
        if (original is null || original.Width <= 0 || original.Height <= 0)
            return null;

        var scale = Math.Min(1.0, Math.Min(
            MaxWidth / (double)original.Width,
            MaxHeight / (double)original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        using var resized = original.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        if (resized is null)
            return null;

        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
            return null;

        var colorHex = computeColor ? ExtractColor(resized) : "";
        return new CoverThumbnail(encoded.ToArray(), colorHex);
    }

    private static string ExtractColor(SKBitmap bitmap)
    {
        var bins = new Dictionary<int, ColorBin>();
        var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 96);

        for (var y = 0; y < bitmap.Height; y += step)
        {
            for (var x = 0; x < bitmap.Width; x += step)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 128)
                    continue;

                var r = (int)pixel.Red;
                var g = (int)pixel.Green;
                var b = (int)pixel.Blue;
                var luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                if (luminance < 0.07 || luminance > 0.93)
                    continue;

                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var saturation = (max - min) / 255.0;
                var weight = 1.0 + saturation * 2.5;

                var key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                if (!bins.TryGetValue(key, out var bin))
                {
                    bin = new ColorBin();
                    bins[key] = bin;
                }

                bin.Weight += weight;
                bin.R += r * weight;
                bin.G += g * weight;
                bin.B += b * weight;
            }
        }

        var best = bins.Values.OrderByDescending(bin => bin.Weight).FirstOrDefault();
        if (best is null || best.Weight < 8)
            return "";

        var color = new SKColor(
            (byte)Math.Clamp(best.R / best.Weight, 0, 255),
            (byte)Math.Clamp(best.G / best.Weight, 0, 255),
            (byte)Math.Clamp(best.B / best.Weight, 0, 255),
            255);

        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }
}
