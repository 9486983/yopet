namespace yopet.Sdk;

internal sealed class NullLocalizer : ILocalizer
{
    public static readonly NullLocalizer Instance = new();

    private NullLocalizer()
    {
    }

    public ILocalizer For(string prefix) => this;

    public string Get(string key) => key;

    public void SetCulture(string cultureName)
    {
    }
}
