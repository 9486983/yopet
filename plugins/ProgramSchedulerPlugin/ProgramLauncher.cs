using System.Diagnostics;

namespace ProgramSchedulerPlugin;

/// <summary>停止结果</summary>
internal enum StopOutcome
{
    /// <summary>成功停止（至少一个进程被关闭）</summary>
    Stopped,
    /// <summary>没有找到运行中的进程</summary>
    NotRunning,
    /// <summary>找到了进程但未能关闭</summary>
    Failed,
}

/// <summary>
/// 外部程序的启动与停止（跨平台）。
/// 优先停止本插件启动的进程；找不到时按可执行文件名回退查找。
/// 停止时先优雅关闭（CloseMainWindow），超时再强制结束进程树，并校验是否真的退出。
/// </summary>
internal sealed class ProgramLauncher
{
    private readonly Dictionary<string, Process> _running = new();
    private readonly object _lock = new();

    /// <summary>启动规则对应的程序。返回 (是否成功, 结果说明)。</summary>
    public Task<(bool Ok, string Message)> LaunchAsync(ScheduleRule rule, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(rule.Path))
            return Task.FromResult<(bool, string)>((false, "PathEmpty"));

        lock (_lock)
        {
            if (_running.TryGetValue(rule.Id, out var existing))
            {
                try
                {
                    if (!existing.HasExited)
                        return Task.FromResult<(bool, string)>((false, "AlreadyRunning"));
                }
                catch { /* 进程状态不可读，按已退出处理 */ }
                _running.Remove(rule.Id);
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = rule.Path,
                Arguments = rule.Arguments ?? "",
                UseShellExecute = false,
            };
            if (!string.IsNullOrWhiteSpace(rule.WorkingDir) && Directory.Exists(rule.WorkingDir))
                psi.WorkingDirectory = rule.WorkingDir;

            var process = Process.Start(psi);
            if (process == null)
                return Task.FromResult<(bool, string)>((false, "StartFailed"));

            lock (_lock) _running[rule.Id] = process;
            log?.Invoke($"launched pid={process.Id} path={rule.Path}");
            return Task.FromResult<(bool, string)>((true, "Started"));
        }
        catch (Exception ex)
        {
            log?.Invoke($"launch failed: {ex.Message}");
            return Task.FromResult<(bool, string)>((false, ex.Message));
        }
    }

    /// <summary>停止规则对应的程序：先优雅关闭，超时再强制结束进程树。</summary>
    public async Task<StopOutcome> StopAsync(ScheduleRule rule, Action<string>? log = null)
    {
        var targets = new List<Process>();
        var seen = new HashSet<int>();

        lock (_lock)
        {
            if (_running.TryGetValue(rule.Id, out var tracked))
            {
                _running.Remove(rule.Id);
                targets.Add(tracked);
                try { seen.Add(tracked.Id); } catch { }
            }
        }

        // 回退：按可执行文件名查找（如 yopet 重启后、或进程非本插件启动）
        var name = SafeProcessName(rule.Path);
        if (!string.IsNullOrEmpty(name))
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { if (seen.Add(p.Id)) targets.Add(p); } catch { }
                }
            }
            catch { /* 枚举失败按未找到处理 */ }
        }

        log?.Invoke($"stop: name=\"{name}\", candidates={targets.Count}");
        if (targets.Count == 0)
            return StopOutcome.NotRunning;

        var stopped = 0;
        var failed = 0;

        foreach (var process in targets)
        {
            try
            {
                if (SafeHasExited(process)) continue;

                var graceful = false;
                try { graceful = process.CloseMainWindow(); } catch { }
                if (graceful)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await process.WaitForExitAsync(cts.Token); } catch { /* 超时走强杀 */ }
                }

                if (!SafeHasExited(process))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception ex) { log?.Invoke($"kill failed pid={process.Id}: {ex.Message}"); }
                }

                // 校验是否真的退出，避免假报成功
                try
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await process.WaitForExitAsync(cts2.Token);
                }
                catch { }

                if (SafeHasExited(process))
                    stopped++;
                else
                {
                    failed++;
                    log?.Invoke($"still running after kill pid={process.Id}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                log?.Invoke($"stop error pid={SafePid(process)}: {ex.Message}");
            }
        }

        log?.Invoke($"stop done: stopped={stopped}, failed={failed}");
        if (stopped > 0) return StopOutcome.Stopped;
        return failed > 0 ? StopOutcome.Failed : StopOutcome.NotRunning;
    }

    /// <summary>该规则对应的进程是否由本插件启动且仍在运行</summary>
    public bool IsRunning(string ruleId)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(ruleId, out var process))
            {
                try { return !process.HasExited; }
                catch { return false; }
            }
        }
        return false;
    }

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    private static int SafePid(Process process)
    {
        try { return process.Id; }
        catch { return -1; }
    }

    private static string SafeProcessName(string path)
    {
        try { return Path.GetFileNameWithoutExtension(path); }
        catch { return ""; }
    }
}
