namespace Queryeasy.Proxy.Tds;

internal enum TdsPacketType : byte
{
    SqlBatch = 0x01,
    RpcRequest = 0x03,
    TabularResult = 0x04,
    Attention = 0x06,
    BulkLoad = 0x07,
    TransactionManagerRequest = 0x0E,
    Login7 = 0x10,
    Sspi = 0x11,
    PreLogin = 0x12
}
