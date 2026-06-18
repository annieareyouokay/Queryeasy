namespace Queryeasy.Proxy.Tds;

internal sealed class RawTlsDetectedException : IOException
{
    public RawTlsDetectedException(byte[] initialBytes)
        : base("Raw TLS stream detected after TDS prelogin; SQL text cannot be inspected without TLS termination.")
    {
        InitialBytes = initialBytes;
    }

    public byte[] InitialBytes { get; }
}
