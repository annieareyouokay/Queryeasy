using Queryeasy.Proxy;
using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds;
using System.Diagnostics;
using System.Text;

namespace Queryeasy.Proxy.Tests;

public class UnitTest1
{
    [Fact]
    public void DateTime2Helper_RoundTripsScaleThree()
    {
        var value = new DateTime(2026, 6, 22, 13, 14, 15, 123);
        var bytes = TdsDateTime2Helper.Encode(value, 3);

        var decoded = TdsDateTime2Helper.Decode(bytes, 3);

        Assert.Equal(value, decoded);
        Assert.Equal(7, bytes.Length);
    }

    [Fact]
    public void DateTime2Helper_EncodesScaleZeroWithShorterValue()
    {
        var value = new DateTime(2026, 6, 22, 13, 14, 15, 987);
        var bytes = TdsDateTime2Helper.Encode(value, 0);

        var decoded = TdsDateTime2Helper.Decode(bytes, 0);

        Assert.Equal(new DateTime(2026, 6, 22, 13, 14, 15), decoded);
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public void DateTime2Helper_RoundTripsScaleSeven()
    {
        var value = new DateTime(2026, 6, 22, 13, 14, 15).AddTicks(1_234_567);
        var bytes = TdsDateTime2Helper.Encode(value, 7);

        var decoded = TdsDateTime2Helper.Decode(bytes, 7);

        Assert.Equal(value, decoded);
        Assert.Equal(8, bytes.Length);
    }

    [Theory]
    [InlineData("@P1", "P1")]
    [InlineData("P1", "P1")]
    public void ParameterNameHelper_NormalizesAtPrefix(string input, string expected)
    {
        Assert.Equal(expected, ParameterNameHelper.Normalize(input));
    }

    [Fact]
    public void ProxyOptions_LoadKeepsRootRewriteRulesAndHardeningDefaults()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Proxy": {
                    "ListenHost": "127.0.0.1",
                    "ListenPort": 11433,
                    "TargetHost": "127.0.0.1",
                    "TargetPort": 1433
                  },
                  "RewriteRules": [
                    {
                      "Name": "ChangeP1",
                      "Enabled": true,
                      "Scope": "RpcSpExecuteSql",
                      "When": {
                        "ParameterExists": "@P1"
                      },
                      "Actions": [
                        {
                          "Type": "SetParameterType",
                          "Name": "@P1",
                          "SqlType": "datetime2(0)"
                        }
                      ]
                    }
                  ]
                }
                """);

            var options = ProxyOptions.Load(path);
            options.Validate();

            Assert.Equal(500, options.MaxConcurrentSessions);
            Assert.Equal(1_048_576, options.MaxInspectableMessageBytes);
            Assert.Equal(65_536, options.MaxRewriteSqlChars);
            Assert.Single(options.RewriteRules);
            Assert.Equal(QueryRewriteScope.RpcSpExecuteSql, options.RewriteRules[0].Scope);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpExecuteSqlParameterHelper_ResolvesUnnamedParameterByDeclaration()
    {
        var request = new RpcSpExecuteSqlRequest(
            [],
            "select @P1",
            "@P1 datetime2(3)",
            [
                CreateParameter("@stmt", "nvarchar(20)", "select @P1", 0, 0, 0, 0),
                CreateParameter("@params", "nvarchar(20)", "@P1 datetime2(3)", 0, 0, 0, 0),
                CreateParameter(string.Empty, "datetime2(3)", "2026-06-22T13:14:15.1230000", 0, 0, 0, 0)
            ]);

        var resolved = SpExecuteSqlParameterHelper.ResolveParameter(request, "@P1");

        Assert.NotNull(resolved);
        Assert.Equal("datetime2(3)", resolved.TypeName);
    }

    [Fact]
    public void SqlRewriter_RejectsSetParameterTypeWithoutSqlType()
    {
        var rewriter = new SqlRewriter(
        [
            new SqlRewriteRule
            {
                Name = "BadRule",
                Actions =
                [
                    new SqlRewriteAction
                    {
                        Type = SqlRewriteActionType.SetParameterType,
                        Name = "@P1"
                    }
                ]
            }
        ]);

        var result = rewriter.Rewrite(
            "select @P1",
            QueryRewriteScope.RpcSpExecuteSql,
            [new RewriteParameterInfo("@P1", "datetime2(3)")]);

        Assert.NotNull(result.Error);
        Assert.Contains("requires SqlType", result.Error);
    }

    [Fact]
    public void SpExecuteSqlParameterHelper_ParseDeclaredParameters_ReadsMultipleDateTime2Parameters()
    {
        var parameters = SpExecuteSqlParameterHelper.ParseDeclaredParameters(
            "@P1 datetime2(3), @P2 datetime2(3), @P3 int");

        Assert.Equal(3, parameters.Count);
        Assert.Equal("@P1", parameters[0].Name);
        Assert.Equal("datetime2(3)", parameters[0].TypeName);
        Assert.Equal("@P2", parameters[1].Name);
        Assert.Equal("datetime2(3)", parameters[1].TypeName);
        Assert.Equal("@P3", parameters[2].Name);
        Assert.Equal("int", parameters[2].TypeName);
    }

    [Fact]
    public void SqlRewriter_MatchesTableAndParameterRegexAndTypeForAllMatchedParameters()
    {
        var rewriter = new SqlRewriter(
        [
            new SqlRewriteRule
            {
                Name = "Reference47_DateTime2Scale3",
                Scope = QueryRewriteScope.RpcSpExecuteSql,
                When = new SqlRewriteCondition
                {
                    SqlContains = "dbo._Reference47",
                    ParameterNameRegex = "@P\\d+",
                    ParameterType = "datetime2(3)"
                },
                Actions =
                [
                    new SqlRewriteAction
                    {
                        Type = SqlRewriteActionType.SetParameterType,
                        SqlType = "datetime2(0)"
                    }
                ]
            }
        ]);

        var sql = """
            SELECT T1._Description
            FROM dbo._Reference47 T1
            WHERE T1._Fld58 = @P1 AND T1._Date = @P2
            """;
        var parameters = new[]
        {
            new RewriteParameterInfo("@P1", "datetime2(3)"),
            new RewriteParameterInfo("@P2", "datetime2(3)"),
            new RewriteParameterInfo("@P3", "int")
        };

        var result = rewriter.Rewrite(sql, QueryRewriteScope.RpcSpExecuteSql, parameters);

        Assert.True(result.Changed);
        Assert.Equal(2, result.ParameterChanges.Count);
        Assert.Contains(result.ParameterChanges, change => change.Name == "@P1" && change.SqlType == "datetime2(0)");
        Assert.Contains(result.ParameterChanges, change => change.Name == "@P2" && change.SqlType == "datetime2(0)");
    }

    [Fact]
    public void SqlRewriter_SkipsWhenParameterTypeDoesNotMatch()
    {
        var rewriter = CreateReference47DateTime2Rule();

        var result = rewriter.Rewrite(
            "SELECT 1 FROM dbo._Reference47 T1 WHERE T1._Fld58 = @P1",
            QueryRewriteScope.RpcSpExecuteSql,
            [new RewriteParameterInfo("@P1", "datetime2(0)")]);

        Assert.False(result.Changed);
    }

    [Fact]
    public void SqlRewriter_SkipsWhenTableDoesNotMatch()
    {
        var rewriter = CreateReference47DateTime2Rule();

        var result = rewriter.Rewrite(
            "SELECT 1 FROM dbo._Reference48 T1 WHERE T1._Fld58 = @P1",
            QueryRewriteScope.RpcSpExecuteSql,
            [new RewriteParameterInfo("@P1", "datetime2(3)")]);

        Assert.False(result.Changed);
    }

    [Fact]
    public void SqlRewriter_BackwardCompatibleWithParameterExistsAndExplicitActionName()
    {
        var rewriter = new SqlRewriter(
        [
            new SqlRewriteRule
            {
                Name = "ChangeP1",
                Scope = QueryRewriteScope.RpcSpExecuteSql,
                When = new SqlRewriteCondition
                {
                    ParameterExists = "@P1"
                },
                Actions =
                [
                    new SqlRewriteAction
                    {
                        Type = SqlRewriteActionType.SetParameterType,
                        Name = "@P1",
                        SqlType = "datetime2(0)"
                    }
                ]
            }
        ]);

        var result = rewriter.Rewrite(
            "select @P1",
            QueryRewriteScope.RpcSpExecuteSql,
            [new RewriteParameterInfo("@P1", "datetime2(3)")]);

        Assert.True(result.Changed);
        Assert.Single(result.ParameterChanges);
        Assert.Equal("@P1", result.ParameterChanges[0].Name);
        Assert.Equal("datetime2(0)", result.ParameterChanges[0].SqlType);
    }

    [Fact]
    public void InspectionCapabilities_ForwardOnlyDisablesInspection()
    {
        var options = new ProxyOptions { Mode = ProxyMode.ForwardOnly };

        var capabilities = options.GetInspectionCapabilities();

        Assert.True(capabilities.IsForwardOnly);
        Assert.False(capabilities.InspectSqlBatch);
        Assert.False(capabilities.InspectRpc);
        Assert.False(capabilities.RewriteSqlBatch);
        Assert.False(capabilities.RewriteRpc);
    }

    [Fact]
    public void InspectionCapabilities_RewriteWithRpcOnlyRulesSkipsSqlBatchInspection()
    {
        var options = new ProxyOptions
        {
            Mode = ProxyMode.Rewrite,
            LogSqlText = false,
            RewriteRules =
            [
                new SqlRewriteRule
                {
                    Name = "RpcOnly",
                    Scope = QueryRewriteScope.RpcSpExecuteSql,
                    When = new SqlRewriteCondition { ParameterExists = "@P1" },
                    Actions =
                    [
                        new SqlRewriteAction
                        {
                            Type = SqlRewriteActionType.SetParameterType,
                            Name = "@P1",
                            SqlType = "datetime2(0)"
                        }
                    ]
                }
            ]
        };

        var capabilities = options.GetInspectionCapabilities();

        Assert.False(capabilities.InspectSqlBatch);
        Assert.True(capabilities.InspectRpc);
        Assert.False(capabilities.RewriteSqlBatch);
        Assert.True(capabilities.RewriteRpc);
    }

    [Fact]
    public void InspectionCapabilities_InspectOnlyAlwaysInspectsBothMessageTypes()
    {
        var options = new ProxyOptions
        {
            Mode = ProxyMode.InspectOnly,
            LogSqlText = false
        };

        var capabilities = options.GetInspectionCapabilities();

        Assert.True(capabilities.InspectSqlBatch);
        Assert.True(capabilities.InspectRpc);
        Assert.False(capabilities.RewriteSqlBatch);
        Assert.False(capabilities.RewriteRpc);
    }

    [Fact]
    public void SqlRewriter_RejectsInvalidParameterNameRegexAtStartup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SqlRewriter(
        [
            new SqlRewriteRule
            {
                Name = "BadRegex",
                When = new SqlRewriteCondition
                {
                    ParameterNameRegex = "@P(["
                },
                Actions =
                [
                    new SqlRewriteAction
                    {
                        Type = SqlRewriteActionType.SetParameterType,
                        Name = "@P1",
                        SqlType = "datetime2(0)"
                    }
                ]
            }
        ]));

        Assert.Contains("When.ParameterNameRegex", exception.Message);
    }

    [Fact]
    public async Task TdsPacketWriter_FlushesOncePerMultiPacketWrite()
    {
        await using var stream = new FlushCountingStream();
        var writer = new TdsPacketWriter(stream);
        var packet = new TdsPacket((byte)TdsPacketType.SqlBatch, 0x01, 0, 1, 0, [0x01, 0x02]);

        await writer.WriteAsync([packet, packet.WithPayload([0x03, 0x04], packetId: 2)], CancellationToken.None);

        Assert.Equal(1, stream.FlushCount);
    }

    [Fact]
    public void RpcRequestInspector_SkipsParameterParseWhenFullParseNotRequired()
    {
        var payload = new byte[] { 0xFF, 0xFF, 0x0A, 0x00, 0x00, 0x00 };

        var result = RpcRequestInspector.Inspect(payload, requiresFullSpExecuteSqlParse: false);

        Assert.True(result.ContainsSpExecuteSql);
        Assert.Null(result.Statement);
        Assert.Empty(result.Parameters);
        Assert.Null(result.SpExecuteSqlRequest);
    }

    [Fact]
    public void RpcSpExecuteSqlEncoder_RewritesDateTime2ScaleAndParameterDeclaration()
    {
        var declaration = "@P1 datetime2(3)";
        var declarationBytes = Encoding.Unicode.GetBytes(declaration);
        var p1Value = new DateTime(2026, 6, 22, 13, 14, 15, 123);
        var p1ValueBytes = TdsDateTime2Helper.Encode(p1Value, 3);
        var payload = new byte[2 + declarationBytes.Length + 5 + p1ValueBytes.Length];
        var offset = 0;
        var paramsLengthOffset = offset;
        payload[offset++] = (byte)declarationBytes.Length;
        payload[offset++] = (byte)(declarationBytes.Length >> 8);
        var paramsValueOffset = offset;
        declarationBytes.CopyTo(payload.AsSpan(offset));
        offset += declarationBytes.Length;
        var p1StartOffset = offset;
        payload[offset++] = 0x00; // unnamed parameter
        payload[offset++] = 0x00; // status
        payload[offset++] = TdsRpcTypeIds.DateTime2;
        payload[offset++] = 0x03; // scale
        payload[offset++] = (byte)p1ValueBytes.Length;
        p1ValueBytes.CopyTo(payload.AsSpan(offset));
        offset += p1ValueBytes.Length;

        var request = new RpcSpExecuteSqlRequest(
            payload,
            "select @P1",
            declaration,
            [
                CreateParameter("@stmt", "nvarchar(20)", "select @P1", 0, 0, 0, 0),
                new(
                    "@params",
                    "nvarchar(20)",
                    declaration,
                    0,
                    new RpcParameterEncoding(
                        0,
                        paramsValueOffset + declarationBytes.Length,
                        0,
                        0,
                        paramsLengthOffset,
                        2,
                        paramsValueOffset,
                        declarationBytes.Length,
                        TdsRpcTypeIds.NVarChar,
                        declarationBytes.Length,
                        null,
                        true,
                        null)),
                new(
                    string.Empty,
                    "datetime2(3)",
                    TdsDateTime2Helper.Format(p1Value),
                    0,
                    new RpcParameterEncoding(
                        p1StartOffset,
                        offset,
                        p1StartOffset + 2,
                        p1StartOffset + 4,
                        p1StartOffset + 4,
                        1,
                        p1StartOffset + 5,
                        p1ValueBytes.Length,
                        TdsRpcTypeIds.DateTime2,
                        null,
                        3,
                        false,
                        null))
            ]);
        var changes = new[]
        {
            new RewriteParameterChange("@P1", null, "datetime2(0)", "TestRule")
        };

        var rewritten = RpcSpExecuteSqlEncoder.Encode(request, request.Statement, changes);
        var rewrittenText = Encoding.Unicode.GetString(rewritten);

        Assert.Contains("datetime2(0)", rewrittenText);
        Assert.True(ContainsSequence(rewritten, [TdsRpcTypeIds.DateTime2, 0x00, 0x06]));
    }

    [Fact]
    public void ProxyPerformanceMetrics_RecordsCountTotalAvgMax()
    {
        var metrics = new ProxyPerformanceMetrics();

        metrics.Record(ProxyPerformanceStage.C2sReadPacket, Stopwatch.Frequency / 10);
        metrics.Record(ProxyPerformanceStage.C2sReadPacket, Stopwatch.Frequency / 5);

        var summary = metrics.BuildSummary();

        Assert.Contains("C2sReadPacket=2/", summary);
        Assert.Contains("/avg", summary);
        Assert.Contains("/max", summary);
    }

    [Fact]
    public void SessionPerformanceTracker_MergesIntoGlobal()
    {
        var metrics = new ProxyPerformanceMetrics();
        var session = metrics.CreateSessionTracker();

        session.Record(ProxyPerformanceStage.SessionConnect, Stopwatch.Frequency / 20);
        session.Complete();

        var summary = metrics.BuildSummary();

        Assert.Contains("SessionConnect=1/", summary);
    }

    [Fact]
    public void NoOpPerformanceRecorder_HasNoSideEffects()
    {
        var recorder = NoOpPerformanceRecorder.Instance;

        using (recorder.Measure(ProxyPerformanceStage.C2sWritePackets))
        {
        }

        recorder.Record(ProxyPerformanceStage.C2sWritePackets, Stopwatch.Frequency);
    }

    private sealed class FlushCountingStream : MemoryStream
    {
        public int FlushCount { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }

    private static SqlRewriter CreateReference47DateTime2Rule()
    {
        return new SqlRewriter(
        [
            new SqlRewriteRule
            {
                Name = "Reference47_DateTime2Scale3",
                Scope = QueryRewriteScope.RpcSpExecuteSql,
                When = new SqlRewriteCondition
                {
                    SqlContains = "dbo._Reference47",
                    ParameterNameRegex = "@P\\d+",
                    ParameterType = "datetime2(3)"
                },
                Actions =
                [
                    new SqlRewriteAction
                    {
                        Type = SqlRewriteActionType.SetParameterType,
                        SqlType = "datetime2(0)"
                    }
                ]
            }
        ]);
    }

    private static RpcParameterInspectionResult CreateParameter(
        string name,
        string typeName,
        string value,
        int valueLengthOffset,
        int valueOffset,
        int valueLength,
        byte typeId)
    {
        return new RpcParameterInspectionResult(
            name,
            typeName,
            value,
            0,
            new RpcParameterEncoding(0, 0, 0, 0, valueLengthOffset, 0, valueOffset, valueLength, typeId, null, null, false, null));
    }

    private static bool ContainsSequence(byte[] source, byte[] expected)
    {
        for (var index = 0; index <= source.Length - expected.Length; index++)
        {
            if (source.AsSpan(index, expected.Length).SequenceEqual(expected))
            {
                return true;
            }
        }

        return false;
    }
}
