namespace Queryeasy.Proxy.Tds.PreLogin;

internal enum TdsEncryptionOption : byte
{
    EncryptOff = 0x00,
    EncryptOn = 0x01,
    EncryptNotSupported = 0x02,
    EncryptRequired = 0x03
}
