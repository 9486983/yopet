using Avalonia.Controls;

namespace yopet.Sdk;

public interface IDialogView
{
    Control View { get; }

    DialogViewOptions Options { get; }

    void Cancel();
}

public interface IDialogView<T> : IDialogView
{
    Task<T?> Result { get; }
}
