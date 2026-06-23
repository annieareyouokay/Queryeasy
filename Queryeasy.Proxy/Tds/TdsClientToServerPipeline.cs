using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds.PreLogin;
using System.Buffers;
using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsClientToServerPipeline
{
    private readonly string _sessionId;
    private readonly ProxyOptions _options;
    private readonly InspectionCapabilities _capabilities;
    private readonly NetworkStream _clientStream;
    private readonly NetworkStream _targetStream;
    private readonly TdsPacketReader _reader;
    private readonly TdsPacketWriter _writer;
    private readonly SqlRewriter _rewriter;
    private readonly ProxyMetrics _metrics;

    public TdsClientToServerPipeline(
        string sessionId,
        NetworkStream clientStream,
        NetworkStream targetStream,
        ProxyOptions options,
        InspectionCapabilities capabilities,
        SqlRewriter rewriter,
        ProxyMetrics metrics)
    {
        _sessionId = sessionId;
        _options = options;
        _capabilities = capabilities;
        _clientStream = clientStream;
        _targetStream = targetStream;
        _reader = new TdsPacketReader(clientStream);
        _writer = new TdsPacketWriter(targetStream);
        _rewriter = rewriter;
        _metrics = metrics;
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
                    ProxyLog.Warn(
                        $"[{_sessionId}] PreLogin TryDisable was applied, but raw TLS was still detected. SQL rewrite unavailable.");
                }

                _metrics.RawTlsFallback();
                return totalBytesSent + await ForwardRawTlsStreamAsync(ex.InitialBytes, cancellationToken);
            }

            if (packet is null)
            {
                ProxyLog.Debug($"[{_sessionId}] client -> sql closed by peer.");
                break;
            }

            var packetsToForward = _capabilities.IsForwardOnly
                ? await ForwardClientMessageAsync(packet, cancellationToken)
                : packet.KnownType switch
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
        ProxyLog.Warn(
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
                    ProxyLog.Debug($"[{_sessionId}] client -> sql raw TLS stream closed by peer.");
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
            ProxyLog.Warn($"[{_sessionId}] client -> sql raw TLS stream stopped after idle timeout.");
        }
        catch (IOException ex)
        {
            ProxyLog.Debug($"[{_sessionId}] client -> sql raw TLS I/O ended: {ex.Message}");
        }
        catch (SocketException ex)
        {
            ProxyLog.Debug($"[{_sessionId}] client -> sql raw TLS socket ended: {ex.Message}");
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

    private async Task<IReadOnlyList<TdsPacket>> ForwardClientMessageAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        if (firstPacket.KnownType == TdsPacketType.SqlBatch)
        {
            _metrics.SqlBatchInspected();
        }
        else if (firstPacket.KnownType == TdsPacketType.RpcRequest)
        {
            _metrics.RpcInspected();
        }

        return await ForwardMessagePacketsAsync(firstPacket, cancellationToken);
    }

    private async Task<IReadOnlyList<TdsPacket>> ForwardMessagePacketsAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        if (firstPacket.IsEndOfMessage)
        {
            return [firstPacket];
        }

        var message = await ReadMessagePacketsAsync(firstPacket, cancellationToken);
        return message.Packets;
    }

    private async Task<IReadOnlyList<TdsPacket>> ProcessSqlBatchMessageAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        _metrics.SqlBatchInspected();

        if (!_capabilities.InspectSqlBatch)
        {
            return await ForwardMessagePacketsAsync(firstPacket, cancellationToken);
        }

        var message = await ReadMessagePacketsAsync(firstPacket, cancellationToken);
        var originalPackets = message.Packets;

        if (message.IsOversized)
        {
            ProxyLog.Warn(
                $"[{_sessionId}] SQL Batch skipped inspection because message payload exceeded {_options.MaxInspectableMessageBytes} bytes.");
            return originalPackets;
        }

        var payload = CombinePayloads(originalPackets);
        var batch = SqlBatchExtractor.Decode(payload);
        var sql = batch.Sql;

        LogSql("SQL Batch", sql);

        if (IsSqlTooLargeForRewrite(sql))
        {
            ProxyLog.Warn($"[{_sessionId}] SQL Batch skipped rewrite because SQL text exceeded {_options.MaxRewriteSqlChars} chars.");
            return originalPackets;
        }

        if (_options.Mode is not (ProxyMode.DryRun or ProxyMode.Rewrite)
            || !_capabilities.RewriteSqlBatch)
        {
            return originalPackets;
        }

        var rewriteResult = _rewriter.Rewrite(sql);

        if (rewriteResult.Error is not null)
        {
            return HandleRewriteFailure("SQL Batch rewrite", rewriteResult.Error, originalPackets);
        }

        if (!rewriteResult.Changed)
        {
            return originalPackets;
        }

        _metrics.RewriteMatched();
        ProxyLog.Info($"[{_sessionId}] SQL Batch matched rewrite rule '{rewriteResult.RuleName}'.");

        if (_options.LogRewriteSqlText)
        {
            LogSql("SQL Batch rewritten", rewriteResult.Sql);
        }

        if (_options.Mode == ProxyMode.DryRun)
        {
            ProxyLog.Info($"[{_sessionId}] DryRun enabled; forwarding original SQL Batch.");
            return originalPackets;
        }

        var rewrittenPayload = SqlBatchExtractor.Encode(batch, rewriteResult.Sql);
        _metrics.RewriteApplied();
        return SplitRewrittenPayload(firstPacket, originalPackets, rewrittenPayload);
    }

    private async Task<IReadOnlyList<TdsPacket>> ProcessRpcRequestMessageAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        _metrics.RpcInspected();

        if (!_capabilities.InspectRpc)
        {
            return await ForwardMessagePacketsAsync(firstPacket, cancellationToken);
        }

        var message = await ReadMessagePacketsAsync(firstPacket, cancellationToken);
        var originalPackets = message.Packets;

        if (message.IsOversized)
        {
            ProxyLog.Warn(
                $"[{_sessionId}] RPC Request skipped inspection because message payload exceeded {_options.MaxInspectableMessageBytes} bytes.");
            return originalPackets;
        }

        var payload = CombinePayloads(originalPackets);
        var requiresFullSpExecuteSqlParse = _capabilities.RewriteRpc || _capabilities.LogSqlText;
        var inspectionResult = RpcRequestInspector.Inspect(payload, requiresFullSpExecuteSqlParse);

        ProxyLog.Debug(
            $"[{_sessionId}] RPC Request inspected. Procedure: {FormatEmpty(inspectionResult.ProcedureName)}, sp_executesql: {inspectionResult.ContainsSpExecuteSql}.");

        if (inspectionResult.ParseWarning is not null)
        {
            _metrics.ParseWarning();
            ProxyLog.Warn($"[{_sessionId}] RPC parse warning: {inspectionResult.ParseWarning}");
        }

        if (_options.LogSqlText && inspectionResult.ContainsSpExecuteSql)
        {
            LogSql("RPC sp_executesql stmt", inspectionResult.Statement ?? "<null>");

            if (!string.IsNullOrEmpty(inspectionResult.ParameterDeclaration))
            {
                LogSql("RPC sp_executesql params", inspectionResult.ParameterDeclaration);
            }

            foreach (var parameter in inspectionResult.Parameters.Skip(2))
            {
                LogSql(
                    $"RPC sp_executesql value {FormatEmpty(parameter.Name)} {parameter.TypeName}",
                    parameter.Value ?? "<null>");
            }
        }

        if (_options.Mode is not (ProxyMode.DryRun or ProxyMode.Rewrite)
            || !_capabilities.RewriteRpc
            || !inspectionResult.ContainsSpExecuteSql
            || inspectionResult.SpExecuteSqlRequest is null)
        {
            return originalPackets;
        }

        if (IsSqlTooLargeForRewrite(inspectionResult.SpExecuteSqlRequest.Statement))
        {
            ProxyLog.Warn($"[{_sessionId}] RPC sp_executesql skipped rewrite because SQL text exceeded {_options.MaxRewriteSqlChars} chars.");
            return originalPackets;
        }

        var rewriteResult = _rewriter.Rewrite(
            inspectionResult.SpExecuteSqlRequest.Statement,
            QueryRewriteScope.RpcSpExecuteSql,
            SpExecuteSqlParameterHelper.GetLogicalParameters(
                inspectionResult.SpExecuteSqlRequest.ParameterDeclaration,
                inspectionResult.SpExecuteSqlRequest.SqlParameters));

        if (rewriteResult.Error is not null)
        {
            return HandleRewriteFailure("RPC sp_executesql rewrite", rewriteResult.Error, originalPackets);
        }

        if (!rewriteResult.Changed)
        {
            return originalPackets;
        }

        _metrics.RewriteMatched();
        ProxyLog.Info(
            $"[{_sessionId}] RPC sp_executesql matched rewrite rule(s): {string.Join(", ", rewriteResult.RuleNames)}.");

        if (_options.LogRewriteSqlText)
        {
            LogSql("RPC sp_executesql rewritten stmt", rewriteResult.Sql);

            foreach (var parameterChange in rewriteResult.ParameterChanges)
            {
                var changeDescription = parameterChange.SqlType is not null
                    ? $"type -> {parameterChange.SqlType}"
                    : "value changed";

                ProxyLog.Info(
                    $"[{_sessionId}] RPC sp_executesql parameter rewrite '{parameterChange.Name}' ({changeDescription}) by rule '{parameterChange.RuleName}'.");
            }
        }

        if (_options.Mode == ProxyMode.DryRun)
        {
            ProxyLog.Info($"[{_sessionId}] DryRun enabled; forwarding original RPC sp_executesql.");
            return originalPackets;
        }

        byte[] rewrittenPayload;

        try
        {
            rewrittenPayload = RpcSpExecuteSqlEncoder.Encode(
                inspectionResult.SpExecuteSqlRequest,
                rewriteResult.Sql,
                rewriteResult.ParameterChanges);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            _metrics.EncodeFailed();
            var error = $"RPC sp_executesql encode failed: {ex.Message}";
            ProxyLog.Warn($"[{_sessionId}] {error}");

            if (_options.RewriteFailureBehavior == RewriteFailureBehavior.FailClosed)
            {
                throw new IOException(error, ex);
            }

            return originalPackets;
        }

        _metrics.RewriteApplied();
        return SplitRewrittenPayload(firstPacket, originalPackets, rewrittenPayload);
    }

    private IReadOnlyList<TdsPacket> HandleRewriteFailure(
        string context,
        string error,
        IReadOnlyList<TdsPacket> originalPackets)
    {
        _metrics.RewriteFailed();
        ProxyLog.Warn($"[{_sessionId}] {context} failed: {error}");

        if (_options.RewriteFailureBehavior == RewriteFailureBehavior.FailClosed)
        {
            throw new IOException(error);
        }

        return originalPackets;
    }

    private static IReadOnlyList<TdsPacket> SplitRewrittenPayload(
        TdsPacket firstPacket,
        IReadOnlyList<TdsPacket> originalPackets,
        byte[] rewrittenPayload)
    {
        var maxOriginalPayloadLength = Math.Max(1, originalPackets.Max(packet => packet.Payload.Length));
        return TdsPacketSplitter.SplitLike(firstPacket, rewrittenPayload, maxOriginalPayloadLength);
    }

    private async Task<TdsMessageReadResult> ReadMessagePacketsAsync(
        TdsPacket firstPacket,
        CancellationToken cancellationToken)
    {
        var packets = new List<TdsPacket> { firstPacket };
        var totalPayloadBytes = firstPacket.Payload.Length;
        var isOversized = totalPayloadBytes > _options.MaxInspectableMessageBytes;

        if (isOversized)
        {
            _metrics.OversizedMessage();
        }

        while (!packets[^1].IsEndOfMessage)
        {
            var nextPacket = await ReadPacketWithIdleTimeoutAsync(cancellationToken)
                ?? throw new IOException("TDS message ended before EndOfMessage packet.");

            packets.Add(nextPacket);
            totalPayloadBytes += nextPacket.Payload.Length;

            if (!isOversized && totalPayloadBytes > _options.MaxInspectableMessageBytes)
            {
                _metrics.OversizedMessage();
                isOversized = true;
            }
        }

        return new TdsMessageReadResult(packets, isOversized);
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
            ProxyLog.Warn($"[{_sessionId}] client -> sql stopped after idle timeout.");
            throw;
        }
    }

    private void LogPacket(string direction, TdsPacket packet)
    {
        var packetType = packet.KnownType?.ToString() ?? $"Unknown 0x{packet.Type:X2}";

        ProxyLog.Debug(
            $"[{_sessionId}] {direction} TDS {packetType}, status=0x{packet.Status:X2}, length={packet.Length}, packetId={packet.PacketId}.");

        if (_options.LogPayloadPreview && _options.PayloadPreviewBytes > 0)
        {
            ProxyLog.Trace($"[{_sessionId}] {direction} payload preview: {BuildPayloadPreview(packet.Payload)}");
        }
    }

    private void LogSql(string label, string sql)
    {
        if (!_options.LogSqlText)
        {
            return;
        }

        ProxyLog.Debug($"[{_sessionId}] {label}: {TrimForLog(sql)}");
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

    private bool IsSqlTooLargeForRewrite(string sql)
    {
        return _options.MaxRewriteSqlChars > 0 && sql.Length > _options.MaxRewriteSqlChars;
    }

    private static string FormatEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? "<empty>" : value;
    }

    private static byte[] CombinePayloads(IEnumerable<TdsPacket> packets)
    {
        return packets.SelectMany(packet => packet.Payload).ToArray();
    }

    private sealed record TdsMessageReadResult(IReadOnlyList<TdsPacket> Packets, bool IsOversized);
}
