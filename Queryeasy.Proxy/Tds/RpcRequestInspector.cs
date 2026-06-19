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
            return new RpcInspectionResult(false, string.Empty, null, null, [], ex.Message);
        }
    }

    private static RpcInspectionResult InspectCore(byte[] payload)
    {
        var offset = GetRpcBodyOffset(payload);

        if (offset >= payload.Length)
        {
            return new RpcInspectionResult(false, string.Empty, null, null, [], "RPC payload does not contain a body.");
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

        return new RpcInspectionResult(
            isSpExecuteSql,
            procedure.DisplayName,
            statement,
            parameterDeclaration,
            parameters,
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
        var nameLength = ReadByte(payload, ref offset);
        var name = ReadUnicodeString(payload, ref offset, nameLength);
        var status = ReadByte(payload, ref offset);
        _ = status;
        var type = ReadTypeInfo(payload, ref offset);
        var value = ReadValue(payload, ref offset, type);

        return new RpcParameterInspectionResult(name, type.DisplayName, value);
    }

    private static RpcTypeInfo ReadTypeInfo(byte[] payload, ref int offset)
    {
        var typeId = ReadByte(payload, ref offset);

        return typeId switch
        {
            0x24 => new RpcTypeInfo(typeId, "uniqueidentifier", 16, null, false),
            0x26 => new RpcTypeInfo(typeId, "intn", ReadByte(payload, ref offset), null, false),
            0x28 => new RpcTypeInfo(typeId, "date", null, null, false),
            0x29 => new RpcTypeInfo(typeId, "time", null, ReadByte(payload, ref offset), false),
            0x2A => new RpcTypeInfo(typeId, "datetime2", null, ReadByte(payload, ref offset), false),
            0x2B => new RpcTypeInfo(typeId, "datetimeoffset", null, ReadByte(payload, ref offset), false),
            0x30 => new RpcTypeInfo(typeId, "tinyint", 1, null, false),
            0x32 => new RpcTypeInfo(typeId, "bit", 1, null, false),
            0x34 => new RpcTypeInfo(typeId, "smallint", 2, null, false),
            0x38 => new RpcTypeInfo(typeId, "int", 4, null, false),
            0x3A => new RpcTypeInfo(typeId, "smalldatetime", 4, null, false),
            0x3B => new RpcTypeInfo(typeId, "real", 4, null, false),
            0x3C => new RpcTypeInfo(typeId, "money", 8, null, false),
            0x3D => new RpcTypeInfo(typeId, "datetime", 8, null, false),
            0x3E => new RpcTypeInfo(typeId, "float", 8, null, false),
            0x68 => new RpcTypeInfo(typeId, "bitn", ReadByte(payload, ref offset), null, false),
            0x6A => ReadDecimalType(payload, ref offset, typeId, "decimaln"),
            0x6C => ReadDecimalType(payload, ref offset, typeId, "numericn"),
            0x6D => new RpcTypeInfo(typeId, "floatn", ReadByte(payload, ref offset), null, false),
            0x6E => new RpcTypeInfo(typeId, "moneyn", ReadByte(payload, ref offset), null, false),
            0x6F => new RpcTypeInfo(typeId, "datetimen", ReadByte(payload, ref offset), null, false),
            0x7F => new RpcTypeInfo(typeId, "bigint", 8, null, false),
            0x22 => ReadTextType(payload, ref offset, typeId, "image", false),
            0x23 => ReadTextType(payload, ref offset, typeId, "text", false),
            0x63 => ReadTextType(payload, ref offset, typeId, "ntext", true),
            0xA5 => ReadVariableType(payload, ref offset, typeId, "varbinary", false),
            0xA7 => ReadVariableType(payload, ref offset, typeId, "varchar", false),
            0xAD => ReadVariableType(payload, ref offset, typeId, "binary", false),
            0xAF => ReadVariableType(payload, ref offset, typeId, "char", false),
            0xE7 => ReadVariableType(payload, ref offset, typeId, "nvarchar", true),
            0xEF => ReadVariableType(payload, ref offset, typeId, "nchar", true),
            _ => throw new RpcParseException($"Unsupported RPC parameter type 0x{typeId:X2}.")
        };
    }

    private static RpcTypeInfo ReadDecimalType(byte[] payload, ref int offset, byte typeId, string typeName)
    {
        var length = ReadByte(payload, ref offset);
        var precision = ReadByte(payload, ref offset);
        var scale = ReadByte(payload, ref offset);

        return new RpcTypeInfo(typeId, $"{typeName}({precision},{scale})", length, scale, false);
    }

    private static RpcTypeInfo ReadTextType(
        byte[] payload,
        ref int offset,
        byte typeId,
        string typeName,
        bool isUnicode)
    {
        var maxLength = checked((int)ReadUInt32(payload, ref offset));

        if (typeId is 0x23 or 0x63)
        {
            Skip(payload, ref offset, 5);
        }

        var displayLength = isUnicode
            ? (maxLength / 2).ToString()
            : maxLength.ToString();

        return new RpcTypeInfo(typeId, $"{typeName}({displayLength})", maxLength, null, isUnicode);
    }

    private static RpcTypeInfo ReadVariableType(
        byte[] payload,
        ref int offset,
        byte typeId,
        string typeName,
        bool isUnicode)
    {
        var maxLength = ReadUInt16(payload, ref offset);

        if (typeId is 0xA7 or 0xAF or 0xE7 or 0xEF)
        {
            Skip(payload, ref offset, 5);
        }

        var displayLength = maxLength == 0xFFFF
            ? "max"
            : isUnicode
                ? (maxLength / 2).ToString()
                : maxLength.ToString();

        return new RpcTypeInfo(typeId, $"{typeName}({displayLength})", maxLength, null, isUnicode);
    }

    private static string? ReadValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        return type.TypeId switch
        {
            0x24 => ReadFixedBytes(payload, ref offset, 16) is { } guidBytes ? new Guid(guidBytes).ToString() : null,
            0x26 or 0x68 or 0x6D or 0x6E or 0x6F => ReadNullableFixedValue(payload, ref offset, type),
            0x30 or 0x32 or 0x34 or 0x38 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x7F => ReadFixedValue(payload, ref offset, type.FixedLength ?? 0, type.TypeId),
            0x6A or 0x6C => ReadDecimalValue(payload, ref offset),
            0x28 => ReadHexValue(payload, ref offset, 3),
            0x29 => ReadHexValue(payload, ref offset, GetTimeValueLength(type.Scale ?? 0)),
            0x2A => ReadHexValue(payload, ref offset, GetTimeValueLength(type.Scale ?? 0) + 3),
            0x2B => ReadHexValue(payload, ref offset, GetTimeValueLength(type.Scale ?? 0) + 5),
            0x22 or 0x23 or 0x63 => ReadTextValue(payload, ref offset, type),
            0xA5 or 0xA7 or 0xAD or 0xAF or 0xE7 or 0xEF => ReadVariableValue(payload, ref offset, type),
            _ => throw new RpcParseException($"Unsupported RPC parameter value type 0x{type.TypeId:X2}.")
        };
    }

    private static string? ReadNullableFixedValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        var valueLength = ReadByte(payload, ref offset);

        if (valueLength == 0)
        {
            return null;
        }

        return ReadFixedValue(payload, ref offset, valueLength, type.TypeId);
    }

    private static string ReadFixedValue(byte[] payload, ref int offset, int length, byte typeId)
    {
        var bytes = ReadFixedBytes(payload, ref offset, length);

        return typeId switch
        {
            0x30 or 0x26 when length == 1 => bytes[0].ToString(),
            0x32 or 0x68 when length == 1 => (bytes[0] != 0).ToString(),
            0x34 or 0x26 when length == 2 => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(),
            0x38 or 0x26 when length == 4 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(),
            0x7F or 0x26 when length == 8 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(),
            0x3B or 0x6D when length == 4 => BitConverter.ToSingle(bytes).ToString("G"),
            0x3E or 0x6D when length == 8 => BitConverter.ToDouble(bytes).ToString("G"),
            _ => $"0x{Convert.ToHexString(bytes)}"
        };
    }

    private static string? ReadDecimalValue(byte[] payload, ref int offset)
    {
        var valueLength = ReadByte(payload, ref offset);

        if (valueLength == 0)
        {
            return null;
        }

        var bytes = ReadFixedBytes(payload, ref offset, valueLength);
        return $"0x{Convert.ToHexString(bytes)}";
    }

    private static string? ReadTextValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        var valueLength = ReadUInt32(payload, ref offset);

        if (valueLength == uint.MaxValue)
        {
            return null;
        }

        var valueBytes = ReadFixedBytes(payload, ref offset, checked((int)valueLength));

        if (type.IsUnicode)
        {
            return SqlEncoding.GetString(valueBytes);
        }

        return type.TypeId == 0x22
            ? $"0x{Convert.ToHexString(valueBytes)}"
            : Encoding.UTF8.GetString(valueBytes);
    }

    private static string ReadHexValue(byte[] payload, ref int offset, int length)
    {
        var bytes = ReadFixedBytes(payload, ref offset, length);
        return $"0x{Convert.ToHexString(bytes)}";
    }

    private static int GetTimeValueLength(byte scale)
    {
        return scale switch
        {
            <= 2 => 3,
            <= 4 => 4,
            _ => 5
        };
    }

    private static string? ReadVariableValue(byte[] payload, ref int offset, RpcTypeInfo type)
    {
        if (type.MaxLength == 0xFFFF)
        {
            return ReadPlpValue(payload, ref offset, type.IsUnicode);
        }

        var valueLength = ReadUInt16(payload, ref offset);

        if (valueLength == 0xFFFF)
        {
            return null;
        }

        var valueBytes = ReadFixedBytes(payload, ref offset, valueLength);

        if (type.IsUnicode)
        {
            return SqlEncoding.GetString(valueBytes);
        }

        return type.TypeId is 0xA5 or 0xAD
            ? $"0x{Convert.ToHexString(valueBytes)}"
            : Encoding.UTF8.GetString(valueBytes);
    }

    private static string? ReadPlpValue(byte[] payload, ref int offset, bool isUnicode)
    {
        var totalLength = ReadUInt64(payload, ref offset);

        if (totalLength == ulong.MaxValue)
        {
            return null;
        }

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
        return isUnicode ? SqlEncoding.GetString(bytes) : $"0x{Convert.ToHexString(bytes)}";
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

    private static void Skip(byte[] payload, ref int offset, int length)
    {
        EnsureAvailable(payload, offset, length);
        offset += length;
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
        bool IsUnicode)
    {
        public int? FixedLength => MaxLength;
    }

    private sealed class RpcParseException(string message) : InvalidOperationException(message);
}
