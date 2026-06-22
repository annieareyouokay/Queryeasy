namespace Queryeasy.Proxy.Tds;

internal static class TdsRpcTypeIds
{
    public const byte UniqueIdentifier = 0x24;
    public const byte IntN = 0x26;
    public const byte Date = 0x28;
    public const byte Time = 0x29;
    public const byte DateTime2 = 0x2A;
    public const byte DateTimeOffset = 0x2B;
    public const byte TinyInt = 0x30;
    public const byte Bit = 0x32;
    public const byte SmallInt = 0x34;
    public const byte Int = 0x38;
    public const byte SmallDateTime = 0x3A;
    public const byte Real = 0x3B;
    public const byte Money = 0x3C;
    public const byte DateTime = 0x3D;
    public const byte Float = 0x3E;
    public const byte NText = 0x63;
    public const byte BitN = 0x68;
    public const byte DecimalN = 0x6A;
    public const byte NumericN = 0x6C;
    public const byte FloatN = 0x6D;
    public const byte MoneyN = 0x6E;
    public const byte DateTimeN = 0x6F;
    public const byte BigInt = 0x7F;
    public const byte NVarChar = 0xE7;
    public const byte NChar = 0xEF;
}
