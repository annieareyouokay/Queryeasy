namespace Queryeasy.Proxy.Tds.PreLogin;

internal sealed class TdsPreLoginOptionEntry
{
    public TdsPreLoginOptionEntry(byte token, ushort offset, ushort length)
    {
        Token = token;
        Offset = offset;
        Length = length;
    }

    public byte Token { get; }

    public ushort Offset { get; }

    public ushort Length { get; }

    public TdsPreLoginOption? KnownOption => Enum.IsDefined(typeof(TdsPreLoginOption), Token)
        ? (TdsPreLoginOption)Token
        : null;
}
