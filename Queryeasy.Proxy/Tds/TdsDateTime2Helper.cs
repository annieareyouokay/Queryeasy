using System.Globalization;
using System.Text.RegularExpressions;

namespace Queryeasy.Proxy.Tds;

internal static class TdsDateTime2Helper
{
    private static readonly Regex ScaleRegex = new(
        @"\(\s*(\d+)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static byte ParseScaleOrDefault(string? sqlType, byte defaultScale = 7)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return defaultScale;
        }

        var match = ScaleRegex.Match(sqlType);

        if (!match.Success)
        {
            return defaultScale;
        }

        if (!byte.TryParse(match.Groups[1].Value, out var scale) || scale > 7)
        {
            throw new InvalidOperationException($"datetime2 scale '{match.Groups[1].Value}' is invalid. Expected 0..7.");
        }

        return scale;
    }

    public static int GetTimeValueLength(byte scale)
    {
        return scale switch
        {
            <= 2 => 3,
            <= 4 => 4,
            _ => 5
        };
    }

    public static DateTime Decode(ReadOnlySpan<byte> bytes, byte scale)
    {
        var timeLength = GetTimeValueLength(scale);

        if (bytes.Length != timeLength + 3)
        {
            throw new InvalidOperationException(
                $"datetime2({scale}) value length {bytes.Length} is invalid. Expected {timeLength + 3} bytes.");
        }

        long timeUnits = 0;

        for (var i = 0; i < timeLength; i++)
        {
            timeUnits |= (long)bytes[i] << (8 * i);
        }

        var days = bytes[timeLength]
            | (bytes[timeLength + 1] << 8)
            | (bytes[timeLength + 2] << 16);
        var ticks = timeUnits * (long)Math.Pow(10, 7 - scale);

        return DateTime.MinValue.Date.AddDays(days).AddTicks(ticks);
    }

    public static byte[] Encode(DateTime value, byte scale)
    {
        var timeLength = GetTimeValueLength(scale);
        var timeUnits = value.TimeOfDay.Ticks / (long)Math.Pow(10, 7 - scale);
        var days = (value.Date - DateTime.MinValue.Date).Days;
        var bytes = new byte[timeLength + 3];

        for (var i = 0; i < timeLength; i++)
        {
            bytes[i] = (byte)(timeUnits >> (8 * i));
        }

        bytes[timeLength] = (byte)days;
        bytes[timeLength + 1] = (byte)(days >> 8);
        bytes[timeLength + 2] = (byte)(days >> 16);

        return bytes;
    }

    public static string Format(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }

    public static string ReplaceTypeInDeclaration(string declaration, string parameterName, string newSqlType)
    {
        var normalizedName = parameterName.StartsWith('@') ? parameterName : $"@{parameterName}";
        var pattern = $@"(?<name>{Regex.Escape(normalizedName)}\s+)(?<type>[A-Za-z_][A-Za-z0-9_]*(\s*\([^)]*\))?)";

        return Regex.Replace(
            declaration,
            pattern,
            match => $"{match.Groups["name"].Value}{newSqlType}",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
