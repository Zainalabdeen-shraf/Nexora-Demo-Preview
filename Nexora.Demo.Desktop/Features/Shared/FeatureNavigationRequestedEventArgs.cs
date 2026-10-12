namespace Nexora.Demo.Desktop.Features.Shared;

public sealed class FeatureNavigationRequestedEventArgs : EventArgs
{
    public FeatureNavigationRequestedEventArgs(
        string sectionKey,
        string tabName,
        string? contextValue = null,
        string? contextKind = null,
        string? systemKey = null)
    {
        SectionKey = sectionKey;
        TabName = tabName;
        ContextValue = contextValue;
        ContextKind = contextKind;
        SystemKey = systemKey;
    }

    public string SectionKey { get; }
    public string TabName { get; }
    public string? ContextValue { get; }
    public string? ContextKind { get; }
    public string? SystemKey { get; }
}
