using System.Text;
using System.Buffers.Binary;

namespace Queryeasy.Proxy.Tds;

internal static class RpcRequestInspector
{
    private static readonly Encoding SqlEncoding = Encoding.Unicode;
    private const ushort SpExecuteSqlProcedureId = 10;

    public static RpcInspectionResult Inspect(byte[] payload)
    {
        try
        {
            return InspectCore(payload);
        }
        catch (RpcParseException ex)
        {
            return new RpcInspectionResult(false, string.Empty, null, null, [], null, ex.Message);
        }
    }

    private static RpcInspectionResult InspectCore(byte[] payload)
    {
        var offset = GetRpcBodyOffset(payload);

        if (offset >= payload.Length)
        {
            return new RpcInspectionResult(false, string.Empty, null, null, [], null, "RPC payload does not contain a body.");
        }

        var procedure = ReadProcedure(payload, ref offset);
        var optionFlags = ReadUInt16(payload, ref offset);
        _ = optionFlags;

        var parameters = new List<RpcParameterInspectionResult>();
        string? parseWarning = null;

        while (offset < payload.Length)
        {
            try
            {
                parameters.Add(ReadParameter(payload, ref offset));
            }
            catch (RpcParseException ex)
            {
                parseWarning = ex.Message;
                break;
            }
        }

        var isSpExecuteSql = procedure.ProcedureId == SpExecuteSqlProcedureId
            || string.Equals(procedure.Name, "sp_executesql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(procedure.Name, "sys.sp_executesql", StringComparison.OrdinalIgnoreCase);
        var statement = isSpExecuteSql && parameters.Count > 0 ? parameters[0].Value : null;
        var parameterDeclaration = isSpExecuteSql && parameters.Count > 1 ? parameters[1].Value : null;
        var request = isSpExecuteSql && statement is not null
            ? new RpcSpExecuteSqlRequest(payload, statement, parameterDeclaration, parameters)
            : null;

        return new RpcInspectionResult(
            isSpExecuteSql,
            procedure.DisplayName,
            statement,
            parameterDeclaration,
            parameters,
            request,
            parseWarning);
    }

    private static int GetRpcBodyOffset(byte[] payload)
    {
        if (payload.Length < 4)
        {
            return 0;
        }

        var allHeadersLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));

        return allHeadersLength >= 4 && allHeadersLength <= payload.Length
            ? (int)allHeadersLength
            : 0;
    }

    private static RpcProcedure ReadProcedure(byte[] payload, ref int offset)
    {
        var procedureNameLength = ReadUInt16(payload, ref offset);

        if (procedureNameLength == 0xFFFF)
        {
            var procedureId = ReadUInt16(payload, ref offset);
            return new RpcProcedure(null, procedureId);
        }

        var procedureName = ReadUnicodeString(payload, ref offset, procedureNameLength);
        return new RpcProcedure(procedureName, null);
    }

    private static RpcParameterInspectionResult ReadParameter(byte[] payload, ref int offset)
    {
        var parameterStartOffset = offset;
        var nameLength = ReadByte(payload, ref offset);
        var name = ReadUnicodeString(payload, ref offset, nameLength);
        var status = ReadByte(payload, ref offset);
        var typeInfoStartOffset = offset;
        var type = ReadTypeInfo(payload, ref offset);
        var typeInfoEndOffset = offset;
        var value = ReadValue(payload, ref offset, type);
        var parameterEndOffset = offset;
        var encoding = new RpcParameterEncoding(
            parameterStartOffset,
            parameterEndOffset,
            typeInfoStartOffset,
            typeInfoEndOffset,
            value.ValueLengthOffset,
            value.ValueLengthSize,
            value.ValueOffset,
            value.ValueLength,
            type.TypeId,
            type.MaxLength,
            type.Scale,
            type.IsUnicode,
            type.Collation);

        return new RpcParameterInspectionResult(name, type.DisplayName, value.Value, status, encoding);
    }

    private static RpcTypeInfo ReadTypeInfo(byte[] payload, ref int offset)
    {
        var typeId = ReadByte(payload, ref offset);

        return typeId switch
        {
            TdsRpcTypeIds.UniqueIdentifier => new RpcTypeInfo(typeId, "uniqueidentifier", 16, null, false, null),
            TdsRpcTypeIds.IntN => new RpcTypeInfo(typeId, "intn", ReadByte(payload, ref offset), null, false, null),
            TdsRpcTypeIds.Date => new RpcTypeInfo(typeId, "date", null, null, false, null),
            TdsRpcTypeIds.Time => ReadScaledType(payload, ref offset, typeId, "time"),
            TdsRpcTypeIds.DateTime2 => ReadScaledType(payload, ref offset, typeId, "datetime2"),
            TdsRpcTypeIds.DateTimeOffset => ReadScaledType(payload, ref offset, typeId, "datetimeoffset"),
            TdsRpcTypeIds.TinyInt => new RpcTypeInfo(typeId, "tinyint", 1, null, false, null),
            TdsRpcTypeIds.Bit => new RpcTypeInfo(typeId, "bit", 1, null, false, null),
            TdsRpcTypeIds.SmallInt => new RpcTypeInfo(typeId, "smallint", 2, null, false, null),
            TdsRpcTypeIds.Int => new RpcTypeInfo(typeId, "int", 4, null, false, null),
            TdsRpcTypeIds.SmallDateTime => new RpcTypeInfo(typeId, "smalldatetime", 4, null, false, null),
            TdsRpcTypeIds.Real => new RpcTypeInfo(typeId, "real", 4, null, false, null),
            TdsRpcTypeIds.Money => new RpcTypeInfo(typeId, "money", 8, null, false, null),
            TdsRpcTypeIds.DateTime => new RpcTypeInfo(typeId, "datetime", 8, null, false, null),
            TdsRpcTypeIds.Float => new RpcTypeInfo(typeId, "float", 8, null, false, null),
            TdsRpcTypeIds.BitN => new RpcTypeInfo(typeId, "bitn", ReadByte(payload, ref offset), null, false, null),
            TdsRpcTypeIds.DecimalN => ReadDecimalType(payload, ref offset, typeId, "decimaln"),
            TdsRpcTypeIds.NumericN => ReadDecimalType(payload, ref offset, typeId, "numericn"),
            TdsRpcTypeIds.FloatN => new RpcTypeInfo(typeId, "floatn", ReadByte(payload, ref offset), null, false, null),
            TdsRpcTypeIds.MoneyN => new RpcTypeInfo(typeId, "moneyn", ReadByte(payload, ref offset), null, false, null),
            TdsRpcTypeIds.DateTimeN => new RpcTypeInfo(typeId, "datetimen", ReadByte(payload, ref offset), null, false, null),
            TdsRpcTypeIds.BigInt => new RpcTypeInfo(typeId, "bigint", 8, null, false, null),
            0x22 => ReadTextType(payload, ref offset, typeId, "image", false),
            0x23 => ReadTextType(payload, ref offset, typeId, "text", false),
            TdsRpcTypeIds.NText => ReadTextType(payload, ref offset, typeId, "ntext", true),
            0xA5 => ReadVariableType(payload, ref offset, typeId, "varbinary", false),
            0xA7 => ReadVariableType(payload, ref offset, typeId, "varchar", false),
            0xAD => ReadVariableType(payload, ref offset, typeId, "binary", false),
            0xAF => ReadVariableType(payload, ref offset, typeId, "char", false),
            TdsRpcTypeIds.NVarChar => ReadVariableType(payload, ref offset, typeId, "nvarchar", true),
            TdsRpcTypeIds.NChar => ReadVariableType(payload, ref offset, typeId, "nchar", true),
            _ => throw new RpcParseException($"Unsupported RPC parameter type 0x{typeId:X2}.")
        };
    }

    private static RpcTypeInfo ReadDecimalType(byte[] payload, ref int offset, byte typeId, string typeName)
    {
        var length = ReadByte(payload, ref offset);
        var precision = ReadByte(payload, ref offset);
        var scale = ReadByte(payload, ref offset);

        return new RpcTypeInfo(typeId, $"{typeName}({precision},{scale})", length, scale, false, null);
    }

    private static RpcTypeInfo ReadScaledType(byte[] payload, ref int offset, byte typeId, string typeName)
    {
        var scale = ReadByte(payload, ref offset);
        return new RpcTypeInfo(typeId, $"{typeName}({scale})", null, scale, false, null);
    }

    private static RpcTypeInfo ReadTextType(
        byte[] payload,
        ref int offset,
        byte typeId,
        string typeName,
        bool isUnicode)
    {
        var maxLength = checked((int)ReadUInt32(payload, ref offset));

        byte[]? collation = null;

        if (typeId is 0x23 or TdsRpcTypeIds.NText)
        {
            collation = ReadFixedBytes(payload, ref offset, 5);
        }

        var displayLength = isUnicode
            ? (maxLength / 2).ToString()
            : maxLength.ToString();

        return new RpcTypeInfo(typeId, $"{typeName}({displayLength})", maxLength, null, isUnicode, collation);
    }

    private static RpcTypeInfo ReadVariableType(
        byte[] payload,
        ref int offset,
        byte typeId,
        string typeName,
        bool isUnicode)
    {
        var maxLength = ReadUInt16(payload, ref offset);

        byte[]? collation = null;

        if (typeId is 0xA7 or 0xAF or TdsRpcTypeIds.NVarChar or TdsRpcTypeIds.NChar)
        {
            collation = ReadFixedBytes(payload, ref offset, 5);
        }

        var displayLength = maxLength == 0xFFFF
            ? "max"
            : isUnicode
                ? (maxLength / 2).ToString()
                : maxLength.ToString();

        return new RpcTypeInfo(typeId, $"{typeName}({displayLength})", maxLength, null, isUnicode, collation);
    }

    private static RpcValueReadResult ReadValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        return type.TypeId switch
        {
            TdsRpcTypeIds.UniqueIdentifier => ReadGuidValue(payload, ref offset),
            TdsRpcTypeIds.IntN or TdsRpcTypeIds.BitN or TdsRpcTypeIds.FloatN or TdsRpcTypeIds.MoneyN or TdsRpcTypeIds.DateTimeN => ReadNullableFixedValue(payload, ref offset, type),
            TdsRpcTypeIds.TinyInt or TdsRpcTypeIds.Bit or TdsRpcTypeIds.SmallInt or TdsRpcTypeIds.Int or TdsRpcTypeIds.SmallDateTime or TdsRpcTypeIds.Real or TdsRpcTypeIds.Money or TdsRpcTypeIds.DateTime or TdsRpcTypeIds.Float or TdsRpcTypeIds.BigInt => ReadFixedValue(payload, ref offset, type.FixedLength ?? 0, type.TypeId),
            TdsRpcTypeIds.DecimalN or TdsRpcTypeIds.NumericN => ReadDecimalValue(payload, ref offset),
            TdsRpcTypeIds.Date => ReadHexValue(payload, ref offset, 3),
            TdsRpcTypeIds.Time => ReadHexValue(payload, ref offset, TdsDateTime2Helper.GetTimeValueLength(type.Scale ?? 0)),
            TdsRpcTypeIds.DateTime2 => ReadDateTime2Value(payload, ref offset, type.Scale ?? 0),
            TdsRpcTypeIds.DateTimeOffset => ReadHexValue(payload, ref offset, TdsDateTime2Helper.GetTimeValueLength(type.Scale ?? 0) + 5),
            0x22 or 0x23 or TdsRpcTypeIds.NText => ReadTextValue(payload, ref offset, type),
            0xA5 or 0xA7 or 0xAD or 0xAF or TdsRpcTypeIds.NVarChar or TdsRpcTypeIds.NChar => ReadVariableValue(payload, ref offset, type),
            _ => throw new RpcParseException($"Unsupported RPC parameter value type 0x{type.TypeId:X2}.")
        };
    }

    private static RpcValueReadResult ReadGuidValue(byte[] payload, ref int offset)
    {
        var valueOffset = offset;
        var bytes = ReadFixedBytes(payload, ref offset, 16);

        return new RpcValueReadResult(new Guid(bytes).ToString(), -1, 0, valueOffset, bytes.Length);
    }

    private static RpcValueReadResult ReadNullableFixedValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        var valueLengthOffset = offset;
        var valueLength = ReadByte(payload, ref offset);

        if (valueLength == 0)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 1, offset, 0);
        }

        return ReadFixedValue(payload, ref offset, valueLength, type.TypeId, valueLengthOffset, 1);
    }

    private static RpcValueReadResult ReadFixedValue(
        byte[] payload,
        ref int offset,
        int length,
        byte typeId,
        int valueLengthOffset = -1,
        int valueLengthSize = 0)
    {
        var valueOffset = offset;
        var bytes = ReadFixedBytes(payload, ref offset, length);
        var value = typeId switch
        {
            TdsRpcTypeIds.TinyInt or TdsRpcTypeIds.IntN when length == 1 => bytes[0].ToString(),
            TdsRpcTypeIds.Bit or TdsRpcTypeIds.BitN when length == 1 => (bytes[0] != 0).ToString(),
            TdsRpcTypeIds.SmallInt or TdsRpcTypeIds.IntN when length == 2 => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(),
            TdsRpcTypeIds.Int or TdsRpcTypeIds.IntN when length == 4 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(),
            TdsRpcTypeIds.BigInt or TdsRpcTypeIds.IntN when length == 8 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(),
            TdsRpcTypeIds.Real or TdsRpcTypeIds.FloatN when length == 4 => BitConverter.ToSingle(bytes).ToString("G"),
            TdsRpcTypeIds.Float or TdsRpcTypeIds.FloatN when length == 8 => BitConverter.ToDouble(bytes).ToString("G"),
            _ => $"0x{Convert.ToHexString(bytes)}"
        };

        return new RpcValueReadResult(value, valueLengthOffset, valueLengthSize, valueOffset, length);
    }

    private static RpcValueReadResult ReadDecimalValue(byte[] payload, ref int offset)
    {
        var valueLengthOffset = offset;
        var valueLength = ReadByte(payload, ref offset);

        if (valueLength == 0)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 1, offset, 0);
        }

        var valueOffset = offset;
        var bytes = ReadFixedBytes(payload, ref offset, valueLength);

        return new RpcValueReadResult($"0x{Convert.ToHexString(bytes)}", valueLengthOffset, 1, valueOffset, valueLength);
    }

    private static RpcValueReadResult ReadTextValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        var valueLengthOffset = offset;
        var valueLength = ReadUInt32(payload, ref offset);

        if (valueLength == uint.MaxValue)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 4, offset, 0);
        }

        var valueOffset = offset;
        var valueBytes = ReadFixedBytes(payload, ref offset, checked((int)valueLength));

        if (type.IsUnicode)
        {
            return new RpcValueReadResult(SqlEncoding.GetString(valueBytes), valueLengthOffset, 4, valueOffset, valueBytes.Length);
        }

        var value = type.TypeId == 0x22
            ? $"0x{Convert.ToHexString(valueBytes)}"
            : Encoding.UTF8.GetString(valueBytes);

        return new RpcValueReadResult(value, valueLengthOffset, 4, valueOffset, valueBytes.Length);
    }

    private static RpcValueReadResult ReadHexValue(byte[] payload, ref int offset, int length)
    {
        var valueOffset = offset;
        var bytes = ReadFixedBytes(payload, ref offset, length);
        return new RpcValueReadResult($"0x{Convert.ToHexString(bytes)}", -1, 0, valueOffset, bytes.Length);
    }

    private static RpcValueReadResult ReadDateTime2Value(byte[] payload, ref int offset, byte scale)
    {
        var valueLengthOffset = offset;
        var length = ReadByte(payload, ref offset);

        if (length == 0)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 1, offset, 0);
        }

        var valueOffset = offset;
        var bytes = ReadFixedBytes(payload, ref offset, length);
        string value;

        try
        {
            value = TdsDateTime2Helper.Format(TdsDateTime2Helper.Decode(bytes, scale));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            value = $"0x{Convert.ToHexString(bytes)}";
        }

        return new RpcValueReadResult(value, valueLengthOffset, 1, valueOffset, bytes.Length);
    }

    private static RpcValueReadResult ReadVariableValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        if (type.MaxLength == 0xFFFF)
        {
            return ReadPlpValue(payload, ref offset, type.IsUnicode);
        }

        var valueLengthOffset = offset;
        var valueLength = ReadUInt16(payload, ref offset);

        if (valueLength == 0xFFFF)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 2, offset, 0);
        }

        var valueOffset = offset;
        var valueBytes = ReadFixedBytes(payload, ref offset, valueLength);

        if (type.IsUnicode)
        {
            return new RpcValueReadResult(SqlEncoding.GetString(valueBytes), valueLengthOffset, 2, valueOffset, valueBytes.Length);
        }

        var value = type.TypeId is 0xA5 or 0xAD
            ? $"0x{Convert.ToHexString(valueBytes)}"
            : Encoding.UTF8.GetString(valueBytes);

        return new RpcValueReadResult(value, valueLengthOffset, 2, valueOffset, valueBytes.Length);
    }

    private static RpcValueReadResult ReadPlpValue(byte[] payload, ref int offset, bool isUnicode)
    {
        var valueLengthOffset = offset;
        var totalLength = ReadUInt64(payload, ref offset);

        if (totalLength == ulong.MaxValue)
        {
            return new RpcValueReadResult(null, valueLengthOffset, 8, offset, 0);
        }

        var valueOffset = offset;
        using var buffer = new MemoryStream();

        while (true)
        {
            var chunkLength = ReadUInt32(payload, ref offset);

            if (chunkLength == 0)
            {
                break;
            }

            var chunk = ReadFixedBytes(payload, ref offset, checked((int)chunkLength));
            buffer.Write(chunk);
        }

        var bytes = buffer.ToArray();
        var value = isUnicode ? SqlEncoding.GetString(bytes) : $"0x{Convert.ToHexString(bytes)}";

        return new RpcValueReadResult(value, valueLengthOffset, 8, valueOffset, offset - valueOffset);
    }

    private static string ReadUnicodeString(byte[] payload, ref int offset, int characterLength)
    {
        var byteLength = checked(characterLength * 2);
        EnsureAvailable(payload, offset, byteLength);
        var value = SqlEncoding.GetString(payload, offset, byteLength);
        offset += byteLength;

        return value;
    }

    private static byte ReadByte(byte[] payload, ref int offset)
    {
        EnsureAvailable(payload, offset, 1);
        return payload[offset++];
    }

    private static ushort ReadUInt16(byte[] payload, ref int offset)
    {
        EnsureAvailable(payload, offset, 2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2));
        offset += 2;

        return value;
    }

    private static uint ReadUInt32(byte[] payload, ref int offset)
    {
        EnsureAvailable(payload, offset, 4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
        offset += 4;

        return value;
    }

    private static ulong ReadUInt64(byte[] payload, ref int offset)
    {
        EnsureAvailable(payload, offset, 8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset, 8));
        offset += 8;

        return value;
    }

    private static byte[] ReadFixedBytes(byte[] payload, ref int offset, int length)
    {
        EnsureAvailable(payload, offset, length);
        var bytes = payload.AsSpan(offset, length).ToArray();
        offset += length;

        return bytes;
    }

    private static void EnsureAvailable(byte[] payload, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > payload.Length)
        {
            throw new RpcParseException("RPC payload ended before the current field was fully read.");
        }
    }

    private sealed record RpcProcedure(string? Name, ushort? ProcedureId)
    {
        public string DisplayName => ProcedureId is not null
            ? ProcedureId == SpExecuteSqlProcedureId
                ? "sp_executesql"
                : $"ProcID:{ProcedureId}"
            : Name ?? string.Empty;
    }

    private sealed record RpcTypeInfo(
        byte TypeId,
        string DisplayName,
        int? MaxLength,
        byte? Scale,
        bool IsUnicode,
        byte[]? Collation)
    {
        public int? FixedLength => MaxLength;
    }

    private sealed record RpcValueReadResult(
        string? Value,
        int ValueLengthOffset,
        int ValueLengthSize,
        int ValueOffset,
        int ValueLength);

    private sealed class RpcParseException(string message) : InvalidOperationException(message);
}
