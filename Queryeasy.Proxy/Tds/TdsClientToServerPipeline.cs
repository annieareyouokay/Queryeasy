using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds.PreLogin;
using System.Buffers;
using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsClientToServerPipeline
{
    private readonly string _sessionId;
    private readonly ProxyOptions _options;
    private readonly NetworkStream _clientStream;
    private readonly NetworkStream _targetStream;
    private readonly TdsPacketReader _reader;
    private readonly TdsPacketWriter _writer;
    private readonly SqlRewriter _rewriter;

    public TdsClientToServerPipeline(
        string sessionId,
        NetworkStream clientStream,
        NetworkStream targetStream,
        ProxyOptions options)
    {
        _sessionId = sessionId;
        _options = options;
        _clientStream = clientStream;
        _targetStream = targetStream;
        _reader = new TdsPacketReader(clientStream);
        _writer = new TdsPacketWriter(targetStream);
        _rewriter = new SqlRewriter(options.RewriteRules);
    }

    public async Task<long> RunAsync(CancellationToken cancellationToken)
    {
        var totalBytesSent = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            TdsPacket? packet;

            try
            {
                packet = await ReadPacketWithIdleTimeoutAsync(cancellationToken);
            }
            catch (RawTlsDetectedException ex)
            {
                if (_options.PreLoginEncryptionMode == PreLoginEncryptionMode.RequirePlainText
                    || _options.FailIfEncryptionRequired)
                {
                    throw new IOException(
                        "Raw TLS stream detected after PreLogin. SQL rewrite requires plaintext TDS, and fail-closed mode is enabled.",
                        ex);
                }

                if (_options.PreLoginEncryptionMode == PreLoginEncryptionMode.TryDisable)
                {
                    Console.WriteLine(
                        $"[{_sessionId}] PreLogin TryDisable was applied, but raw TLS was still detected. SQL rewrite unavailable.");
                }

                return totalBytesSent + await ForwardRawTlsStreamAsync(ex.InitialBytes, cancellationToken);
            }

            if (packet is null)
            {
                Console.WriteLine($"[{_sessionId}] client -> sql closed by peer.");
                break;
            }

            var packetsToForward = packet.KnownType switch
            {
                TdsPacketType.SqlBatch => await ProcessSqlBatchMessageAsync(packet, cancellationToken),
                TdsPacketType.RpcRequest => await ProcessRpcRequestMessageAsync(packet, cancellationToken),
                _ => [packet]
            };

            foreach (var packetToForward in packetsToForward)
            {
                LogPacket("client -> sql", packetToForward);
            }

            await _writer.WriteAsync(packetsToForward, cancellationToken);
            totalBytesSent += packetsToForward.Sum(packetToForward => packetToForward.Length);
        }

        return totalBytesSent;
    }

    private async Task<long> ForwardRawTlsStreamAsync(byte[] initialBytes, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[{_sessionId}] Raw TLS stream detected; switching client -> sql to byte forwarding. SQL text is not visible in this session.");

        await _targetStream.WriteAsync(initialBytes, cancellationToken);
        await _targetStream.FlushAsync(cancellationToken);

        var totalBytesSent = (long)initialBytes.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(_options.BufferSizeBytes);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await ReadRawWithIdleTimeoutAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    Console.WriteLine($"[{_sessionId}] client -> sql raw TLS stream closed by peer.");
                    break;
                }

                await _targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                await _targetStream.FlushAsync(cancellationToken);
                totalBytesSent += bytesRead;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{_sessionId}] client -> sql raw TLS stream stopped after idle timeout.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[{_sessionId}] client -> sql raw TLS I/O ended: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[{_sessionId}] client -> sql raw TLS socket ended: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return totalBytesSent;
    }

    private async Task<int> ReadRawWithIdleTimeoutAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCancellation.CancelAfter(_options.IdleTimeout);

        return await _clientStream.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCancellation.Token);
    }

    private async Task<IReadOnlyList<TdsPacket>> ProcessSqlBatchMessageAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        var originalPackets = await ReadMessagePacketsAsync(firstPacket, cancellationToken);
        var payload = CombinePayloads(originalPackets);
        var batch = SqlBatchExtractor.Decode(payload);
        var sql = batch.Sql;

        LogSql("SQL Batch", sql);

        if (_options.Mode is not (ProxyMode.DryRun or ProxyMode.Rewrite))
        {
            return originalPackets;
        }

        var rewriteResult = _rewriter.Rewrite(sql);

        if (rewriteResult.Error is not null)
        {
            Console.WriteLine($"[{_sessionId}] SQL Batch rewrite failed: {rewriteResult.Error}");

            if (_options.RewriteFailureBehavior == RewriteFailureBehavior.FailClosed)
            {
                throw new IOException(rewriteResult.Error);
            }

            return originalPackets;
        }

        if (!rewriteResult.Changed)
        {
            return originalPackets;
        }

        Console.WriteLine($"[{_sessionId}] SQL Batch matched rewrite rule '{rewriteResult.RuleName}'.");

        if (_options.LogRewriteSqlText)
        {
            LogSql("SQL Batch rewritten", rewriteResult.Sql);
        }

        if (_options.Mode == ProxyMode.DryRun)
        {
            Console.WriteLine($"[{_sessionId}] DryRun enabled; forwarding original SQL Batch.");
            return originalPackets;
        }

        var rewrittenPayload = SqlBatchExtractor.Encode(batch, rewriteResult.Sql);
        var maxOriginalPayloadLength = Math.Max(1, originalPackets.Max(packet => packet.Payload.Length));

        return TdsPacketSplitter.SplitLike(firstPacket, rewrittenPayload, maxOriginalPayloadLength);
    }

    private async Task<IReadOnlyList<TdsPacket>> ProcessRpcRequestMessageAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        var originalPackets = await ReadMessagePacketsAsync(firstPacket, cancellationToken);
        var payload = CombinePayloads(originalPackets);
        var inspectionResult = RpcRequestInspector.Inspect(payload);

        Console.WriteLine(
            $"[{_sessionId}] RPC Request inspected. sp_executesql candidate: {inspectionResult.ContainsSpExecuteSql}.");

        if (_options.LogSqlText && inspectionResult.ContainsSpExecuteSql)
        {
            LogSql("RPC Unicode preview", inspectionResult.UnicodePreview);
        }

        return originalPackets;
    }

    private async Task<IReadOnlyList<TdsPacket>> ReadMessagePacketsAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        var packets = new List<TdsPacket> { firstPacket };

        while (!packets[^1].IsEndOfMessage)
        {
            var nextPacket = await ReadPacketWithIdleTimeoutAsync(cancellationToken)
                ?? throw new IOException("TDS message ended before EndOfMessage packet.");

            packets.Add(nextPacket);
        }

        return packets;
    }

    private async Task<TdsPacket?> ReadPacketWithIdleTimeoutAsync(CancellationToken cancellationToken)
    {
        using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCancellation.CancelAfter(_options.IdleTimeout);

        try
        {
            return await _reader.ReadAsync(idleCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[{_sessionId}] client -> sql stopped after idle timeout.");
            throw;
        }
    }

    private void LogPacket(string direction, TdsPacket packet)
    {
        var packetType = packet.KnownType?.ToString() ?? $"Unknown 0x{packet.Type:X2}";

        Console.WriteLine(
            $"[{_sessionId}] {direction} TDS {packetType}, status=0x{packet.Status:X2}, length={packet.Length}, packetId={packet.PacketId}.");

        if (_options.LogPayloadPreview && _options.PayloadPreviewBytes > 0)
        {
            Console.WriteLine($"[{_sessionId}] {direction} payload preview: {BuildPayloadPreview(packet.Payload)}");
        }
    }

    private void LogSql(string label, string sql)
    {
        if (!_options.LogSqlText)
        {
            return;
        }

        Console.WriteLine($"[{_sessionId}] {label}: {TrimForLog(sql)}");
    }

    private string BuildPayloadPreview(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return "<empty>";
        }

        var previewLength = Math.Min(payload.Length, _options.PayloadPreviewBytes);
        var preview = Convert.ToHexString(payload.AsSpan(0, previewLength));
        return payload.Length > previewLength ? $"{preview}..." : preview;
    }

    private string TrimForLog(string value)
    {
        var normalized = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return _options.MaxSqlLogChars > 0 && normalized.Length > _options.MaxSqlLogChars
            ? $"{normalized[.._options.MaxSqlLogChars]}..."
            : normalized;
    }

    private static byte[] CombinePayloads(IEnumerable<TdsPacket> packets)
    {
        return packets.SelectMany(packet => packet.Payload).ToArray();
    }
}
