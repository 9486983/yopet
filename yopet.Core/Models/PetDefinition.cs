namespace yopet.Core.Models;

/// <summary>宠物定义（全部来自 Petdex）</summary>
public class PetDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SpritesheetPath { get; set; } = "";
    public int FrameWidth { get; set; } = 192;
    public int FrameHeight { get; set; } = 208;
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 9;
    public int FrameDurationMs { get; set; } = 100;
    public List<string> AnimationStates { get; set; } = new();
}
