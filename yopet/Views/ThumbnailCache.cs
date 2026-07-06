using SkiaSharp;
using yopet.Core.Models;

namespace yopet.Views;

/// <summary>Petdex 宠物缩略图缓存管理</summary>
internal static class ThumbnailCache
{
    private static readonly string ThumbDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".petdex", "thumbs");

    /// <summary>缩略图路径</summary>
    public static string GetThumbPath(string petId)
    {
        var slug = petId.Replace("petdex:", "");
        return Path.Combine(ThumbDir, $"{slug}.png");
    }

    /// <summary>确保缩略图已生成，返回路径</summary>
    public static string EnsureThumbnail(PetDefinition pet)
    {
        var slug = pet.Id.Replace("petdex:", "");
        var thumbPath = GetThumbPath(pet.Id);
        if (File.Exists(thumbPath)) return thumbPath;

        if (!Directory.Exists(ThumbDir))
            Directory.CreateDirectory(ThumbDir);

        try
        {
            using var src = SKBitmap.Decode(pet.SpritesheetPath);
            if (src == null) return thumbPath;

            using var frame = new SKBitmap(pet.FrameWidth, pet.FrameHeight);
            src.ExtractSubset(frame, new SKRectI(0, 0, pet.FrameWidth, pet.FrameHeight));

            using var resized = frame.Resize(new SKImageInfo(80, 86), new SKSamplingOptions(SKFilterMode.Linear));
            if (resized == null) return thumbPath;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.OpenWrite(thumbPath);
            data.SaveTo(fs);
        }
        catch { }

        return thumbPath;
    }
}
