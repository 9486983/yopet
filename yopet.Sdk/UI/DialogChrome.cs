using Avalonia;
using Avalonia.Controls;

namespace yopet.Sdk;

/// <summary>
/// Optional custom-view chrome options used by the host dialog window.
/// </summary>
public static class DialogChrome
{
    private sealed class DialogChromeOwner : AvaloniaObject
    {
    }

    public static readonly AttachedProperty<double> InsetProperty =
        AvaloniaProperty.RegisterAttached<DialogChromeOwner, Control, double>("Inset", 0d);

    public static readonly AttachedProperty<bool> HostDragBarProperty =
        AvaloniaProperty.RegisterAttached<DialogChromeOwner, Control, bool>("HostDragBar", true);

    public static readonly AttachedProperty<double> ButtonInsetProperty =
        AvaloniaProperty.RegisterAttached<DialogChromeOwner, Control, double>("ButtonInset", 6d);

    public static double GetInset(Control control) => control.GetValue(InsetProperty);

    public static void SetInset(Control control, double value) => control.SetValue(InsetProperty, value);

    public static bool GetHostDragBar(Control control) => control.GetValue(HostDragBarProperty);

    public static void SetHostDragBar(Control control, bool value) => control.SetValue(HostDragBarProperty, value);

    public static double GetButtonInset(Control control) => control.GetValue(ButtonInsetProperty);

    public static void SetButtonInset(Control control, double value) => control.SetValue(ButtonInsetProperty, value);
}
