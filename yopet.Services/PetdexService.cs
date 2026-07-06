using yopet.Core.Interfaces;
using yopet.Core.Models;

namespace yopet.Services;

/// <summary>
/// Petdex 宠物加载器 —— 扫描本地 ~/.codex/pets/ 和 ~/.petdex/pets/
/// </summary>
public class PetdexService : IPetdexService
{
    private static readonly string[] Roots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "pets"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".petdex", "pets"),
    ];

    /// <summary>获取本地已安装的所有宠物 ID（去重）</summary>
    public List<string> GetInstalledPetIds()
    {
        var ids = new HashSet<string>();
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name != null && File.Exists(Path.Combine(dir, "pet.json")))
                    ids.Add(name);
            }
        }
        return ids.OrderBy(id => id).ToList();
    }

    /// <summary>加载指定宠物的 manifest（扫描所有根目录）</summary>
    public PetdexManifest? LoadManifest(string petId)
    {
        foreach (var root in Roots)
        {
            var path = Path.Combine(root, petId, "pet.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<PetdexManifest>(json);
            }
            catch { return null; }
        }
        return null;
    }

    /// <summary>获取 spritesheet 文件路径（优先 webp 再 png）</summary>
    public string? GetSpritesheetPath(string petId)
    {
        foreach (var root in Roots)
        {
            var petDir = Path.Combine(root, petId);
            if (!Directory.Exists(petDir)) continue;

            // 先查 pet.json 里声明的路径
            var manifest = LoadManifest(petId);
            if (manifest != null && !string.IsNullOrEmpty(manifest.SpritesheetPath))
            {
                var resolved = Path.Combine(petDir, manifest.SpritesheetPath);
                if (File.Exists(resolved)) return resolved;
            }

            // 回退到标准文件名
            var webp = Path.Combine(petDir, "spritesheet.webp");
            if (File.Exists(webp)) return webp;
            var png = Path.Combine(petDir, "spritesheet.png");
            if (File.Exists(png)) return png;
        }
        return null;
    }

    /// <summary>将宠物加载为 PetDefinition</summary>
    public PetDefinition? ToPetDefinition(string petId)
    {
        var manifest = LoadManifest(petId);
        if (manifest == null) return null;
        var spritePath = GetSpritesheetPath(petId);
        if (string.IsNullOrEmpty(spritePath)) return null;

        return new PetDefinition
        {
            Id = $"petdex:{petId}",
            Name = manifest.DisplayName,
            Description = manifest.Description,
            SpritesheetPath = spritePath,
            FrameWidth = manifest.FrameWidth,
            FrameHeight = manifest.FrameHeight,
            Columns = manifest.Columns,
            Rows = manifest.Rows,
            FrameDurationMs = manifest.FrameDurationMs,
            AnimationStates = [],
        };
    }
}
