namespace yopet.Sdk;

internal sealed class NullPluginLogger : IPluginLogger
{
    public static readonly NullPluginLogger Instance = new();

    private NullPluginLogger()
    {
    }

    public string LogPath { get; set; } = "";

    public IPluginLogger For(string source) => this;

    public void Debug(string message)
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? ex = null)
    {
    }

}
