using System.Text.Json;
using yopet.Core.Interfaces;
using yopet.Core.Models;

namespace yopet.Services;

public class ConfigService : IConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yopet");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public AppConfig Config { get; private set; }

    public ConfigService()
    {
        Config = Load();
    }

    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                    return cfg;
            }
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
        }
    }

    public string? GetPluginValue(string key)
    {
        Config.PluginValues.TryGetValue(key, out var val);
        return val;
    }

    public void SetPluginValue(string key, string value)
    {
        Config.PluginValues[key] = value;
        Save();
    }

    public void SetPluginValuesBatch(IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (key, value) in values)
        {
            if (value != null)
                Config.PluginValues[key] = value;
            else
                Config.PluginValues.Remove(key);
        }
        Save();
    }
}
