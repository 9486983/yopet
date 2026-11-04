namespace yopet.Sdk;

public sealed class DialogViewOptions
{
    public bool ShowClose { get; init; } = true;

    public bool AlignToBubbleBottom { get; init; }

    public Func<double>? BottomInset { get; init; }
}
