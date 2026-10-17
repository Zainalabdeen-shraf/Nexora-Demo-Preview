namespace Nexora.Demo.Desktop.Features.NetworkOperations.Connections;

/// <summary>In-memory active RouterOS connection shared by the demo screens. Secrets are not persisted here.</summary>
public static class RouterConnectionSession
{
    public static event EventHandler? Changed;

    public static RouterOsConnectionOptions? CurrentOptions { get; private set; }
    public static RouterOsConnectionSnapshot? CurrentSnapshot { get; private set; }

    public static void SetConnected(RouterOsConnectionOptions options, RouterOsConnectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshot);
        CurrentOptions = options;
        CurrentSnapshot = snapshot;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Disconnect()
    {
        if (CurrentOptions is null && CurrentSnapshot is null) return;
        CurrentOptions = null;
        CurrentSnapshot = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}