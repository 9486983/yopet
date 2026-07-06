using System.Text.Json.Serialization;

namespace yopet.Core.Models;

/// <summary>
/// Petdex 宠物清单 —— 对应 pet.json（字段名匹配实际 JSON 格式）
/// </summary>
public class PetdexManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("spritesheetPath")]
    public string SpritesheetPath { get; set; } = "";

    // ── 标准 Petdex 规格（不在 JSON 中，硬编码） ──
    [JsonIgnore]
    public int FrameWidth { get; set; } = 192;
    [JsonIgnore]
    public int FrameHeight { get; set; } = 208;
    [JsonIgnore]
    public int Columns { get; set; } = 8;
    [JsonIgnore]
    public int Rows { get; set; } = 9;
    [JsonIgnore]
    public int FrameDurationMs { get; set; } = 100;
}
