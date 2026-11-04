using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System.Reflection;
using TKCode.Arc;
using yopet.Sdk;
using yopet.UI.Animation;

namespace yopet.Views;

public sealed class AgentApprovalView : IDialogView<string?>
{
    private static readonly PropertyInfo? SurfaceProperty = typeof(AgentStatus).GetProperty(
        "Surface",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
        };

        IDisposable? heightFollower = null;
        void FollowSurface()
        {
            if (heightFollower is not null)
                return;

            var surface = FindSurface(status);
            if (surface is null)
                return;

            heightFollower = MotionKit.FollowHeight(surface, content, 32);
        }

        FollowSurface();
        status.TemplateApplied += (_, _) => FollowSurface();
        content.DetachedFromVisualTree += (_, _) =>
        {
            heightFollower?.Dispose();
            heightFollower = null;
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

    private static Control? FindSurface(AgentStatus status)
    {
        if (SurfaceProperty?.GetValue(status) is Control surface)
            return surface;

        return TopLevel.GetTopLevel(status)?
            .GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => control.GetType().Name == "AgentStatusSurface");
    }
}
