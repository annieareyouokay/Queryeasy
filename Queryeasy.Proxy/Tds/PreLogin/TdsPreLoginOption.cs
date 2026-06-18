namespace Queryeasy.Proxy.Tds.PreLogin;

internal enum TdsPreLoginOption : byte
{
    Version = 0x00,
    Encryption = 0x01,
    Instance = 0x02,
    ThreadId = 0x03,
    Mars = 0x04,
    TraceId = 0x05,
    FederatedAuthenticationRequired = 0x06,
    NonceOption = 0x07,
    Terminator = 0xFF
}
