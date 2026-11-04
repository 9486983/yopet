using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc.Motion;

namespace yopet.UI.Animation;

public static class MotionKit
{
    public static IDisposable FollowHeight(Control source, Control target, double extra = 0) =>
        new HeightFollower(source, target, extra);

    public static void PrepareReveal(Control control, double offsetY = 8, double fromOpacity = 0)
    {
        control.Opacity = fromOpacity;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (control.RenderTransform is TranslateTransform translate)
            translate.Y = offsetY;
        else
            control.RenderTransform = new TranslateTransform(0, offsetY);
    }

    public static void PlayReveal(
        Control control,
        double offsetY = 8,
        double fromOpacity = 0,
        ArcSpring spring = ArcSpring.Snappy,
        TimeSpan? delay = null)
    {
        var translate = control.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform(0, offsetY);
            control.RenderTransform = translate;
        }

        control.Opacity = fromOpacity;
        var progress = new MotionValue<double>(control, 0, MotionVectors.Double);
        progress.Changed += value =>
        {
            control.Opacity = fromOpacity + (1 - fromOpacity) * value;
            translate.Y = offsetY * (1 - value);
        };

        _ = progress.AnimateTo(1, MotionSpec.Spring(spring, delay ?? TimeSpan.Zero));
    }

    public static void Reveal(
        Control control,
        double offsetY = 8,
        double fromOpacity = 0,
        ArcSpring spring = ArcSpring.Snappy)
    {
        PrepareReveal(control, offsetY, fromOpacity);
        PlayReveal(control, offsetY, fromOpacity, spring);
    }

    public static void PrepareStagger(
        IEnumerable<Control> controls,
        double offsetY = 8,
        double fromOpacity = 0)
    {
        foreach (var control in controls)
            PrepareReveal(control, offsetY, fromOpacity);
    }

    public static void PlayStagger(
        IEnumerable<Control> controls,
        double offsetY = 8,
        double fromOpacity = 0,
        int staggerMs = 20,
        ArcSpring spring = ArcSpring.Snappy)
    {
        var list = controls as IReadOnlyList<Control> ?? controls.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Min(i, 12) * staggerMs);
            PlayReveal(list[i], offsetY, fromOpacity, spring, delay);
        }
    }

    public static void StaggerReveal(
        IEnumerable<Control> controls,
        double offsetY = 8,
        double fromOpacity = 0,
        int staggerMs = 20,
        ArcSpring spring = ArcSpring.Snappy)
    {
        PrepareStagger(controls, offsetY, fromOpacity);
        PlayStagger(controls, offsetY, fromOpacity, staggerMs, spring);
    }

    private sealed class HeightFollower : IDisposable
    {
        private readonly Control _source;
        private readonly Control _target;
        private readonly double _extra;
        private bool _disposed;

        public HeightFollower(Control source, Control target, double extra)
        {
            _source = source;
            _target = target;
            _extra = extra;
            _source.LayoutUpdated += OnLayoutUpdated;
            _source.PropertyChanged += OnPropertyChanged;
            Update();
        }

        private void OnLayoutUpdated(object? sender, EventArgs e) => Update();

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Layoutable.HeightProperty || e.Property == Layoutable.BoundsProperty)
                Update();
        }

        private void Update()
        {
            if (_disposed) return;

            var height = _source.Height;
            if (!double.IsFinite(height) || height <= 0)
                height = _source.Bounds.Height;
            if (!double.IsFinite(height) || height <= 0)
                return;

            var wanted = Math.Max(0, height + _extra);
            if (Math.Abs(_target.Height - wanted) > 0.1)
                _target.Height = wanted;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source.LayoutUpdated -= OnLayoutUpdated;
            _source.PropertyChanged -= OnPropertyChanged;
        }
    }
}
