namespace Queryeasy.Proxy.Tds.PreLogin;

internal sealed class TdsPreLoginNegotiationResult
{
    public TdsPreLoginNegotiationResult(long clientToServerBytes, long serverToClientBytes, bool handled)
    {
        ClientToServerBytes = clientToServerBytes;
        ServerToClientBytes = serverToClientBytes;
        Handled = handled;
    }

    public long ClientToServerBytes { get; }

    public long ServerToClientBytes { get; }

    public bool Handled { get; }

    public static TdsPreLoginNegotiationResult NotHandled { get; } = new(0, 0, false);
}
