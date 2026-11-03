namespace yopet.Sdk;

public enum PromptOptionKind
{
    Secondary,
    Primary,
    Danger,
}

public sealed class PromptOption
{
    public string Label { get; init; } = "";

    public string Description { get; init; } = "";

    public PromptOptionKind Kind { get; init; } = PromptOptionKind.Secondary;
}

public sealed class PromptDialogConfig
{
    public string Title { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<PromptOption> Options { get; init; } = [];

    public bool AllowMultiple { get; init; }

    public bool AllowCustom { get; init; }

    public string? CustomLabel { get; init; }

    public string? CustomPlaceholder { get; init; }

    public string ConfirmLabel { get; init; } = "Confirm";

    public string CancelLabel { get; init; } = "Cancel";

    public string? ValidationMessage { get; init; }
}
