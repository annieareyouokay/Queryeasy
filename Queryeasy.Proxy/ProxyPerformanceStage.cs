namespace Queryeasy.Proxy;

internal enum ProxyPerformanceStage
{
    SessionConnect,
    SessionPreLogin,
    SessionClientToServer,
    SessionServerToClient,
    PreLoginClientRead,
    PreLoginTargetRead,
    PreLoginWrite,
    C2sReadPacket,
    C2sReadMessage,
    C2sWritePackets,
    C2sSqlBatchDecode,
    C2sSqlBatchRewrite,
    C2sSqlBatchEncodeSplit,
    C2sRpcInspect,
    C2sRpcRewrite,
    C2sRpcEncodeSplit,
    C2sRawTlsForward,
    S2cRead,
    S2cWrite,
}
