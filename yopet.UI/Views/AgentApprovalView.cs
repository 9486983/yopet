using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reflection;
using TKCode.Arc;
using yopet.Sdk;

namespace yopet.Views;

public sealed class AgentApprovalView : IDialogView<string?>
{
    private readonly TaskCompletionSource<string?> _result = new();

    public AgentApprovalView(AgentApprovalConfig config)
    {
        var status = new AgentStatus
        {
            Status = AgentStatusRunState.NeedsInput,
            Activity = new AgentStatusActivity(config.Activity, null),
            StartedAt = DateTimeOffset.UtcNow,
            Agent = new AgentModel(config.AgentName, null, null),
            Actions = AgentStatusActions.Approve | AgentStatusActions.Deny | AgentStatusActions.OpenRun,
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Center,
            Labels = new AgentStatusLabels
            {
                Approve = config.ApproveLabel,
                Deny = config.DenyLabel,
                OpenRun = config.AlwaysLabel,
            },
        };

        var detail = string.IsNullOrWhiteSpace(config.Description)
            ? config.Title
            : config.Description;
        if (!string.IsNullOrWhiteSpace(detail))
            ToolTip.SetTip(status, detail);

        status.ApproveRequested += (_, _) => _result.TrySetResult(config.OnceValue);
        status.DenyRequested += (_, _) => _result.TrySetResult(config.RejectValue);
        status.OpenRunRequested += (_, _) => _result.TrySetResult(config.AlwaysValue);

        var content = new StackPanel
        {
            Width = 448,
            Height = 72,
            Children = { status },
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Layoutable.HeightProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
            },
        };

        status.ExpandedChanged += async (_, e) =>
        {
            if (!e.IsExpanded)
            {
                // Let the host follow the panel's own collapse animation instead
                // of snapping the window; keep the shadow inset outside layout.
                content.Height = Math.Max(status.Bounds.Height, 40) + 32;
                return;
            }

            var lastHeight = 0d;
            for (var i = 0; i < 20 && status.IsExpanded; i++)
            {
                await Task.Delay(50);

                var panelHeight = FindPanelHeight(status);
                if (panelHeight <= 0)
                    continue;

                if (Math.Abs(panelHeight - lastHeight) < 0.5 && i >= 3)
                    break;

                lastHeight = panelHeight;
                // The panel is an overlay and is not part of SizeToContent.
                // Leave room below the measured surface for its floating shadow.
                content.Height = panelHeight + 32;
            }
        };

        View = content;
    }

    public Control View { get; }

    public DialogViewOptions Options { get; } = new()
    {
        ShowClose = false,
        AlignToBubbleBottom = true,
        BottomInset = () => 32,
    };

    public Task<string?> Result => _result.Task;

    public void Cancel() => _result.TrySetResult(null);

    private static double FindPanelHeight(AgentStatus status)
    {
        var surfaceProperty = status.GetType().GetProperty(
            "Surface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (surfaceProperty?.GetValue(status) is Control surface)
            return surface.Bounds.Height;

        var root = TopLevel.GetTopLevel(status);
        if (root == null)
            return 0;

        var panel = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.GetType().Name == "AgentStatusSurface")
            .OrderByDescending(control => control.Bounds.Height)
            .FirstOrDefault();

        return panel?.Bounds.Height ?? 0;
    }
}
