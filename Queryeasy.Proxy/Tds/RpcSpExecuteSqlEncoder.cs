using Queryeasy.Proxy.Rewrite;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Queryeasy.Proxy.Tds;

internal static class RpcSpExecuteSqlEncoder
{
    private static readonly Encoding SqlEncoding = Encoding.Unicode;
    private static readonly Encoding TextEncoding = Encoding.UTF8;
    private static readonly byte[] DefaultCollation = [0x09, 0x04, 0xD0, 0x00, 0x00];

    public static byte[] Encode(
        RpcSpExecuteSqlRequest request,
        string statement,
        IReadOnlyList<RewriteParameterChange> parameterChanges)
    {
        var replacements = new List<PayloadReplacement>();
        var parameterDeclaration = request.ParameterDeclaration;

        if (!string.Equals(request.Statement, statement, StringComparison.Ordinal))
        {
            replacements.Add(BuildValueReplacement(request.Parameters[0], statement));
        }

        foreach (var changeGroup in parameterChanges.GroupBy(change => ParameterNameHelper.Normalize(change.Name)))
        {
            var parameter = SpExecuteSqlParameterHelper.ResolveParameter(request, changeGroup.Key)
                ?? throw new InvalidOperationException($"RPC parameter '{changeGroup.Key}' was not found.");

            var value = changeGroup.LastOrDefault(change => change.Value is not null)?.Value ?? parameter.Value;
            var sqlType = changeGroup.LastOrDefault(change => change.SqlType is not null)?.SqlType;

            if (sqlType is not null && parameterDeclaration is not null)
            {
                parameterDeclaration = TdsDateTime2Helper.ReplaceTypeInDeclaration(
                    parameterDeclaration,
                    changeGroup.Key,
                    sqlType);
            }

            replacements.Add(sqlType is null
                ? BuildValueReplacement(parameter, value)
                : BuildParameterReplacement(parameter, sqlType, value, FindCollation(request)));
        }

        if (parameterDeclaration is not null
            && request.Parameters.Count > 1
            && !string.Equals(request.ParameterDeclaration, parameterDeclaration, StringComparison.Ordinal))
        {
            replacements.Add(BuildValueReplacement(request.Parameters[1], parameterDeclaration));
        }

        return ApplyReplacements(request.Payload, replacements);
    }

    private static PayloadReplacement BuildValueReplacement(RpcParameterInspectionResult parameter, string? value)
    {
        var encodedValue = EncodeValueForExistingType(parameter, value);
        var encoding = parameter.Encoding;

        if (encoding.ValueLengthSize > 0)
        {
            var bytes = new byte[encoding.ValueLengthSize + encodedValue.Value.Length];
            WriteLength(bytes.AsSpan(0, encoding.ValueLengthSize), encodedValue.LengthValue);
            encodedValue.Value.CopyTo(bytes.AsSpan(encoding.ValueLengthSize));

            return new PayloadReplacement(
                encoding.ValueLengthOffset,
                encoding.ValueOffset + encoding.ValueLength,
                bytes);
        }

        return new PayloadReplacement(
            encoding.ValueOffset,
            encoding.ValueOffset + encoding.ValueLength,
            encodedValue.Value);
    }

    private static PayloadReplacement BuildParameterReplacement(
        RpcParameterInspectionResult parameter,
        string sqlType,
        string? value,
        byte[] collation)
    {
        var bytes = EncodeParameter(parameter.Name, parameter.Status, sqlType, value, collation);
        var encoding = parameter.Encoding;

        return new PayloadReplacement(encoding.ParameterStartOffset, encoding.ParameterEndOffset, bytes);
    }

    private static EncodedValue EncodeValueForExistingType(RpcParameterInspectionResult parameter, string? value)
    {
        var encoding = parameter.Encoding;

        return encoding.TypeId switch
        {
            TdsRpcTypeIds.UniqueIdentifier => new EncodedValue(EncodeGuid(value), 16),
            TdsRpcTypeIds.IntN => EncodeNullableInteger(value, encoding.MaxLength ?? 4),
            0x30 => new EncodedValue([ParseByte(value)], 1),
            0x32 or 0x68 => EncodeNullableBit(value, encoding.TypeId == 0x68),
            0x34 => new EncodedValue(EncodeInt16(value), 2),
            TdsRpcTypeIds.Int => new EncodedValue(EncodeInt32(value), 4),
            TdsRpcTypeIds.BigInt => new EncodedValue(EncodeInt64(value), 8),
            TdsRpcTypeIds.DateTime2 => EncodeDateTime2Value(value, encoding.Scale ?? 7),
            TdsRpcTypeIds.NText or TdsRpcTypeIds.NVarChar or TdsRpcTypeIds.NChar => EncodeUnicodeText(value),
            0xA7 or 0xAF => EncodeAnsiText(value),
            _ => throw new InvalidOperationException(
                $"RPC parameter '{parameter.Name}' type '{parameter.TypeName}' cannot be rewritten yet.")
        };
    }

    private static byte[] EncodeParameter(
        string name,
        byte status,
        string sqlType,
        string? value,
        byte[] collation)
    {
        using var stream = new MemoryStream();
        WriteParameterHeader(stream, name, status);

        switch (NormalizeSqlType(sqlType))
        {
            case "int":
                stream.WriteByte(TdsRpcTypeIds.IntN);
                stream.WriteByte(4);
                WriteNullableIntegerValue(stream, value, 4);
                break;

            case "bigint":
                stream.WriteByte(TdsRpcTypeIds.IntN);
                stream.WriteByte(8);
                WriteNullableIntegerValue(stream, value, 8);
                break;

            case "bit":
                stream.WriteByte(TdsRpcTypeIds.BitN);
                stream.WriteByte(1);
                WriteNullableBitValue(stream, value);
                break;

            case "nvarchar":
                WriteNVarCharParameter(stream, value, collation);
                break;

            case "ntext":
                WriteNTextParameter(stream, value, collation);
                break;

            case "uniqueidentifier":
                stream.WriteByte(TdsRpcTypeIds.UniqueIdentifier);
                stream.Write(EncodeGuid(value));
                break;

            case "datetime2":
                WriteDateTime2Parameter(stream, value, sqlType);
                break;

            default:
                throw new InvalidOperationException($"SQL type '{sqlType}' is not supported for RPC parameter rewrite.");
        }

        return stream.ToArray();
    }

    private static void WriteParameterHeader(Stream stream, string name, byte status)
    {
        var normalizedName = name ?? string.Empty;

        if (normalizedName.Length > byte.MaxValue)
        {
            throw new InvalidOperationException($"RPC parameter name '{normalizedName}' is too long.");
        }

        stream.WriteByte((byte)normalizedName.Length);
        stream.Write(SqlEncoding.GetBytes(normalizedName));
        stream.WriteByte(status);
    }

    private static void WriteNVarCharParameter(Stream stream, string? value, byte[] collation)
    {
        stream.WriteByte(TdsRpcTypeIds.NVarChar);
        var valueBytes = value is null ? [] : SqlEncoding.GetBytes(value);
        var maxLength = checked((ushort)Math.Max(valueBytes.Length, 1));
        WriteUInt16(stream, maxLength);
        stream.Write(collation);

        if (value is null)
        {
            WriteUInt16(stream, 0xFFFF);
            return;
        }

        WriteUInt16(stream, checked((ushort)valueBytes.Length));
        stream.Write(valueBytes);
    }

    private static void WriteNTextParameter(Stream stream, string? value, byte[] collation)
    {
        stream.WriteByte(TdsRpcTypeIds.NText);
        var valueBytes = value is null ? [] : SqlEncoding.GetBytes(value);
        WriteUInt32(stream, checked((uint)Math.Max(valueBytes.Length, 1)));
        stream.Write(collation);

        if (value is null)
        {
            WriteUInt32(stream, uint.MaxValue);
            return;
        }

        WriteUInt32(stream, checked((uint)valueBytes.Length));
        stream.Write(valueBytes);
    }

    private static void WriteDateTime2Parameter(Stream stream, string? value, string sqlType)
    {
        if (value is null)
        {
            throw new InvalidOperationException("datetime2 RPC parameter rewrite does not support null values.");
        }

        var dateTime = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var scale = TdsDateTime2Helper.ParseScaleOrDefault(sqlType);
        var bytes = TdsDateTime2Helper.Encode(dateTime, scale);
        stream.WriteByte(TdsRpcTypeIds.DateTime2);
        stream.WriteByte(scale);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private static EncodedValue EncodeNullableInteger(string? value, int length)
    {
        if (value is null)
        {
            return new EncodedValue([], 0);
        }

        return new EncodedValue(length switch
        {
            1 => [ParseByte(value)],
            2 => EncodeInt16(value),
            4 => EncodeInt32(value),
            8 => EncodeInt64(value),
            _ => throw new InvalidOperationException($"Nullable integer length {length} is not supported.")
        }, length);
    }

    private static EncodedValue EncodeNullableBit(string? value, bool hasLengthPrefix)
    {
        if (value is null)
        {
            return hasLengthPrefix
                ? new EncodedValue([], 0)
                : new EncodedValue([0], 1);
        }

        return new EncodedValue([ParseBoolean(value) ? (byte)1 : (byte)0], 1);
    }

    private static EncodedValue EncodeUnicodeText(string? value)
    {
        return value is null
            ? new EncodedValue([], ushort.MaxValue)
            : new EncodedValue(SqlEncoding.GetBytes(value), SqlEncoding.GetByteCount(value));
    }

    private static EncodedValue EncodeAnsiText(string? value)
    {
        return value is null
            ? new EncodedValue([], ushort.MaxValue)
            : new EncodedValue(TextEncoding.GetBytes(value), TextEncoding.GetByteCount(value));
    }

    private static EncodedValue EncodeDateTime2Value(string? value, byte scale)
    {
        if (value is null)
        {
            return new EncodedValue([], 0);
        }

        var dateTime = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var bytes = TdsDateTime2Helper.Encode(dateTime, scale);

        return new EncodedValue(bytes, bytes.Length);
    }

    private static void WriteNullableIntegerValue(Stream stream, string? value, int length)
    {
        var encoded = EncodeNullableInteger(value, length);
        stream.WriteByte((byte)encoded.LengthValue);
        stream.Write(encoded.Value);
    }

    private static void WriteNullableBitValue(Stream stream, string? value)
    {
        var encoded = EncodeNullableBit(value, true);
        stream.WriteByte((byte)encoded.LengthValue);
        stream.Write(encoded.Value);
    }

    private static byte[] EncodeGuid(string? value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new InvalidOperationException($"Value '{value}' is not a valid uniqueidentifier.");
        }

        return guid.ToByteArray();
    }

    private static byte[] EncodeInt16(string? value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, short.Parse(value ?? "0", CultureInfo.InvariantCulture));
        return bytes;
    }

    private static byte[] EncodeInt32(string? value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, int.Parse(value ?? "0", CultureInfo.InvariantCulture));
        return bytes;
    }

    private static byte[] EncodeInt64(string? value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, long.Parse(value ?? "0", CultureInfo.InvariantCulture));
        return bytes;
    }

    private static byte ParseByte(string? value)
    {
        return byte.Parse(value ?? "0", CultureInfo.InvariantCulture);
    }

    private static bool ParseBoolean(string value)
    {
        return value == "1"
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] FindCollation(RpcSpExecuteSqlRequest request)
    {
        return request.Parameters
            .Select(parameter => parameter.Encoding.Collation)
            .FirstOrDefault(collation => collation is not null)
            ?? DefaultCollation;
    }

    private static byte[] ApplyReplacements(byte[] payload, IReadOnlyList<PayloadReplacement> replacements)
    {
        if (replacements.Count == 0)
        {
            return payload;
        }

        var ordered = replacements.OrderBy(replacement => replacement.Start).ToArray();
        using var stream = new MemoryStream();
        var offset = 0;

        foreach (var replacement in ordered)
        {
            if (replacement.Start < offset)
            {
                throw new InvalidOperationException("RPC rewrite produced overlapping payload replacements.");
            }

            stream.Write(payload.AsSpan(offset, replacement.Start - offset));
            stream.Write(replacement.Bytes);
            offset = replacement.End;
        }

        stream.Write(payload.AsSpan(offset));
        return stream.ToArray();
    }

    private static void WriteLength(Span<byte> destination, int value)
    {
        switch (destination.Length)
        {
            case 1:
                destination[0] = checked((byte)value);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(destination, checked((uint)value));
                break;
            default:
                throw new InvalidOperationException($"Length prefix size {destination.Length} is not supported.");
        }
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value));
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static string NormalizeSqlType(string sqlType)
    {
        var normalized = sqlType.Trim().ToLowerInvariant();
        var lengthStart = normalized.IndexOf('(', StringComparison.Ordinal);

        return lengthStart >= 0 ? normalized[..lengthStart] : normalized;
    }

    private sealed record EncodedValue(byte[] Value, int LengthValue);

    private sealed record PayloadReplacement(int Start, int End, byte[] Bytes);
}
