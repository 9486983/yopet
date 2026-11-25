using SkiaSharp;

namespace EbookReaderPlugin.Core;

public static class EbookImageProcessor
{
    public static (int Width, int Height)? GetPixelSize(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;

        try
        {
            using var encoded = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(encoded);
            if (codec is null)
                return null;

            var info = codec.Info;
            return info.Width > 0 && info.Height > 0 ? (info.Width, info.Height) : null;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? ToGrayscale(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;

        try
        {
            using var source = SKBitmap.Decode(data);
            if (source is null || source.Width <= 0 || source.Height <= 0)
                return null;

            var info = new SKImageInfo(
                source.Width,
                source.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var target = new SKBitmap(info);
            using var canvas = new SKCanvas(target);
            using var paint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                {
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0, 0, 0, 1, 0,
                }),
            };
            canvas.DrawBitmap(source, 0, 0, paint);

            using var image = SKImage.FromBitmap(target);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            return encoded?.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
