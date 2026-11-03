using System.Threading.Channels;
using yopet.Core.Models;
using yopet.Sdk;

namespace ImagePickerPlugin;

[Plugin("文件流转", Version = "1.0.0")]
public class ImagePickerPlugin : PluginBase
{
    private readonly object _gate = new();
    private string _actionName = "";
    private Channel<string[]>? _queue;
    private ISession? _session;
    private CancellationTokenSource? _sessionCts;

    public ImagePickerPlugin(PluginContext context) : base(context)
    {
    }

    public override string Name => T("Name");

    public override string Description => T("Description");

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);

        _actionName = T("ActionName");

        host.RegisterAction(new PluginAction
        {
            Name = _actionName,
            Emoji = "🗂️",
            Description = T("ActionDescription"),
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.Folder,
            FileCallback = async paths =>
            {
                try
                {
                    await OnDropAsync(paths, host);
                }
                catch (Exception ex)
                {
                    host.ShowThought(T("ErrorTitle"), ex.Message);
                }
            },
        });
    }

    private async Task OnDropAsync(string[] paths, IPluginHost host)
    {
        if (paths.Length == 0)
            return;

        var activeSession = _session;
        var queue = _queue;
        if (activeSession?.IsActive == true && queue != null)
        {
            queue.Writer.TryWrite(paths);
            host.ShowReaction("📥");
            return;
        }

        if (!paths.Any(Directory.Exists))
        {
            host.ShowThought(T("NeedFolderTitle"), T("NeedFolderMsg"));
            return;
        }

        var session = host.StartSession(_actionName);
        var channel = Channel.CreateUnbounded<string[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _session = session;
            _queue = channel;
            _sessionCts = cts;
        }

        session.Status = T("WaitingStatus");
        session.Progress = -1;
        session.OnCancelled += _ => cts.Cancel();
        session.OnCompleted += _ => cts.Cancel();

        channel.Writer.TryWrite(paths);
        host.ShowThought(T("StartedTitle"), T("StartedMsg"));

        try
        {
            await host.RunWithAnimation(
                new[] { PetAnimation.Running, PetAnimation.Wave, PetAnimation.Jump },
                async token =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token);
                    try
                    {
                        await RunResidentLoopAsync(channel.Reader, session, host, linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        if (session.IsActive)
                            session.Cancel();
                        channel.Writer.TryComplete();
                        ClearSession(session);
                    }
                });
        }
        catch (Exception ex)
        {
            host.ShowThought(T("ErrorTitle"), ex.Message);
        }
        finally
        {
            if (session.IsActive)
                session.Cancel();
            channel.Writer.TryComplete();
            ClearSession(session);
            cts.Dispose();
        }

        host.ShowThought(T("CanceledTitle"), T("CanceledMsg"));
    }

    private async Task RunResidentLoopAsync(
        ChannelReader<string[]> reader,
        ISession session,
        IPluginHost host,
        CancellationToken token)
    {
        await foreach (var paths in reader.ReadAllAsync(token))
        {
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    if (Directory.Exists(path))
                    {
                        await SortFolderAsync(path, session, host, token);
                    }
                    else if (File.Exists(path) && ImagePickerService.IsJpg(path))
                    {
                        await MoveJpgAsync(path, session, host, token);
                    }
                    else
                    {
                        host.ShowThought(T("SkipTitle"), string.Format(T("SkipMsg"), path));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    host.ShowThought(T("ErrorTitle"), $"{Path.GetFileName(path)}\n{ex.Message}");
                }
            }
        }
    }

    private async Task SortFolderAsync(
        string folder,
        ISession session,
        IPluginHost host,
        CancellationToken token)
    {
        var result = await Task.Run(() => ImagePickerService.SortByExtension(folder, token), token);
        session.Status = string.Format(T("SortedStatus"), result.MovedCount);
        host.ShowThought(
            T("SortedTitle"),
            string.Format(T("SortedMsg"), result.MovedCount, result.FolderNames.Count));
        host.ShowReaction("🗂️");
    }

    private async Task MoveJpgAsync(
        string jpgPath,
        ISession session,
        IPluginHost host,
        CancellationToken token)
    {
        var result = await Task.Run(() => ImagePickerService.MoveJpgToWait(jpgPath, token), token);
        session.Status = T("WaitingStatus");

        if (result.RawTargets.Count == 0)
        {
            host.ShowThought(
                T("JpgNoRawTitle"),
                string.Format(T("JpgNoRawMsg"), result.JpgTarget));
            host.ShowReaction("⚠️");
            return;
        }

        host.ShowThought(
            T("JpgDoneTitle"),
            string.Format(
                T("JpgDoneMsg"),
                result.JpgTarget,
                string.Join("\n", result.RawTargets.Select(Path.GetFileName))));
        host.ShowReaction("✅");
    }

    private void ClearSession(ISession session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
                return;

            _session = null;
            _queue = null;
            _sessionCts = null;
        }
    }

    public override Task CleanupAsync()
    {
        CancellationTokenSource? cts;
        ISession? session;

        lock (_gate)
        {
            cts = _sessionCts;
            session = _session;
        }

        cts?.Cancel();
        session?.Cancel();
        return base.CleanupAsync();
    }
}
