using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds.PreLogin;

internal sealed class TdsPreLoginNegotiator
{
    private readonly string _sessionId;
    private readonly ProxyOptions _options;
    private readonly IPerformanceRecorder _performance;
    private readonly TdsPacketReader _clientReader;
    private readonly TdsPacketReader _targetReader;
    private readonly TdsPacketWriter _clientWriter;
    private readonly TdsPacketWriter _targetWriter;

    public TdsPreLoginNegotiator(
        string sessionId,
        NetworkStream clientStream,
        NetworkStream targetStream,
        ProxyOptions options,
        IPerformanceRecorder performance)
    {
        _sessionId = sessionId;
        _options = options;
        _performance = performance;
        _clientReader = new TdsPacketReader(clientStream);
        _targetReader = new TdsPacketReader(targetStream);
        _clientWriter = new TdsPacketWriter(clientStream);
        _targetWriter = new TdsPacketWriter(targetStream);
    }

    public async Task<TdsPreLoginNegotiationResult> NegotiateAsync(CancellationToken cancellationToken)
    {
        if (_options.PreLoginEncryptionMode == PreLoginEncryptionMode.PassThrough && !_options.LogPreLoginOptions)
        {
            return TdsPreLoginNegotiationResult.NotHandled;
        }

        TdsPacket? clientPacket;
        using (_performance.Measure(ProxyPerformanceStage.PreLoginClientRead))
        {
            clientPacket = await _clientReader.ReadAsync(cancellationToken);
        }

        if (clientPacket is null)
        {
            return TdsPreLoginNegotiationResult.NotHandled;
        }

        if (clientPacket.KnownType != TdsPacketType.PreLogin)
        {
            using (_performance.Measure(ProxyPerformanceStage.PreLoginWrite))
            {
                await _targetWriter.WriteAsync(clientPacket, cancellationToken);
            }

            return new TdsPreLoginNegotiationResult(clientPacket.Length, 0, true);
        }

        var clientPacketToForward = ProcessPreLoginPacket("client", clientPacket);

        using (_performance.Measure(ProxyPerformanceStage.PreLoginWrite))
        {
            await _targetWriter.WriteAsync(clientPacketToForward, cancellationToken);
        }

        TdsPacket? targetPacket;
        using (_performance.Measure(ProxyPerformanceStage.PreLoginTargetRead))
        {
            targetPacket = await _targetReader.ReadAsync(cancellationToken);
        }

        if (targetPacket is null)
        {
            return new TdsPreLoginNegotiationResult(clientPacketToForward.Length, 0, true);
        }

        var targetPacketToForward = ProcessPreLoginPacket("server", targetPacket);

        using (_performance.Measure(ProxyPerformanceStage.PreLoginWrite))
        {
            await _clientWriter.WriteAsync(targetPacketToForward, cancellationToken);
        }

        return new TdsPreLoginNegotiationResult(
            clientPacketToForward.Length,
            targetPacketToForward.Length,
            true);
    }

    private TdsPacket ProcessPreLoginPacket(string side, TdsPacket packet)
    {
        TdsPreLoginMessage message;

        try
        {
            message = TdsPreLoginParser.Parse(packet.Payload);
        }
        catch (IOException ex)
        {
            ProxyLog.Warn($"[{_sessionId}] PreLogin {side} parse failed: {ex.Message}. Forwarding unchanged.");
            return packet;
        }

        var before = message.Encryption;
        var after = before;
        var packetToForward = packet;

        if (_options.PreLoginEncryptionMode is PreLoginEncryptionMode.TryDisable or PreLoginEncryptionMode.RequirePlainText)
        {
            var modifiedMessage = message.WithEncryption(TdsEncryptionOption.EncryptNotSupported);
            after = modifiedMessage.Encryption;
            packetToForward = packet.WithPayload(modifiedMessage.Payload);
        }

        if (_options.LogPreLoginOptions)
        {
            ProxyLog.Debug(
                $"[{_sessionId}] PreLogin {side} ENCRYPTION: {FormatEncryption(before)} -> {FormatEncryption(after)} ({_options.PreLoginEncryptionMode}).");
        }

        return packetToForward;
    }

    private static string FormatEncryption(TdsEncryptionOption? encryption)
    {
        return encryption?.ToString() ?? "missing/unknown";
    }
}
