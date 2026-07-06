using yopet.Core.Interfaces;
using yopet.Core.Models;

namespace yopet.Services;

/// <summary>
/// AI 助手活动监控 —— 读取 ~/.petdex/events/ 事件文件
/// </summary>
public class ActivityMonitor : IActivityMonitor
{
    private static readonly string EventsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".petdex", "events");

    private string _lastEventId = "";

    public void Start()
    {
        try { Directory.CreateDirectory(EventsDir); }
        catch { }
    }

    public void Stop() { }

    /// <summary>读取新事件文件，返回自上次调用以来的增量</summary>
    public AgentEvent[] GetNewEvents()
    {
        if (!Directory.Exists(EventsDir)) return [];

        try
        {
            var files = Directory.GetFiles(EventsDir, "*.json")
                .OrderBy(f => f)
                .ToList();

            var events = new List<AgentEvent>();
            var foundLast = string.IsNullOrEmpty(_lastEventId);

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var ev = System.Text.Json.JsonSerializer.Deserialize<AgentEvent>(json);
                    if (ev == null) continue;

                    if (foundLast)
                        events.Add(ev);
                    else if (ev.Id == _lastEventId)
                        foundLast = true;
                }
                catch { }
            }

            if (events.Count > 0)
                _lastEventId = events[^1].Id;

            CleanupOldFiles(files);
            return events.ToArray();
        }
        catch { return []; }
    }

    private static void CleanupOldFiles(List<string> files)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var file in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { }
        }
    }
}
