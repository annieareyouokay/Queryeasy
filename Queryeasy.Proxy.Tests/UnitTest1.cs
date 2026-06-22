using Queryeasy.Proxy;
using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds;
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

        var result = rewriter.Rewrite("select @P1", QueryRewriteScope.RpcSpExecuteSql, ["@P1"]);

        Assert.NotNull(result.Error);
        Assert.Contains("requires SqlType", result.Error);
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
