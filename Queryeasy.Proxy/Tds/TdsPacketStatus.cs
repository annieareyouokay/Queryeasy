namespace Queryeasy.Proxy.Tds;

[Flags]
internal enum TdsPacketStatus : byte
{
    Normal = 0x00,
    EndOfMessage = 0x01,
    Ignore = 0x02,
    ResetConnection = 0x08,
    ResetConnectionSkipTran = 0x10
}
