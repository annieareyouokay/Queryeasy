namespace Queryeasy.Proxy.Tds.PreLogin;

internal sealed class TdsPreLoginMessage
{
    public TdsPreLoginMessage(byte[] payload, IReadOnlyList<TdsPreLoginOptionEntry> options)
    {
        Payload = payload;
        Options = options;
    }

    public byte[] Payload { get; }

    public IReadOnlyList<TdsPreLoginOptionEntry> Options { get; }

    public TdsEncryptionOption? Encryption
    {
        get
        {
            var encryptionOption = FindOption(TdsPreLoginOption.Encryption);

            if (encryptionOption is null || encryptionOption.Length < 1)
            {
                return null;
            }

            var value = Payload[encryptionOption.Offset];

            return Enum.IsDefined(typeof(TdsEncryptionOption), value)
                ? (TdsEncryptionOption)value
                : null;
        }
    }

    public TdsPreLoginOptionEntry? FindOption(TdsPreLoginOption option)
    {
        return Options.FirstOrDefault(entry => entry.Token == (byte)option);
    }

    public TdsPreLoginMessage WithEncryption(TdsEncryptionOption encryption)
    {
        var encryptionOption = FindOption(TdsPreLoginOption.Encryption);

        if (encryptionOption is null || encryptionOption.Length < 1)
        {
            return this;
        }

        var payload = (byte[])Payload.Clone();
        payload[encryptionOption.Offset] = (byte)encryption;

        return new TdsPreLoginMessage(payload, Options);
    }
}
