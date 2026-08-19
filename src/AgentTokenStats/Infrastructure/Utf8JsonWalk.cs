using System.Globalization;
using System.Text.Json;

namespace AgentTokenStats.Infrastructure;

internal static class Utf8JsonWalk
{
    public static bool TryStartObject(ReadOnlySpan<byte> utf8, out Utf8JsonReader reader)
    {
        reader = new Utf8JsonReader(utf8);
        try
        {
            return reader.Read() && reader.TokenType == JsonTokenType.StartObject;
        }
        catch (JsonException)
        {
            reader = default;
            return false;
        }
    }

    public static bool NextProperty(ref Utf8JsonReader reader, out ReadOnlySpan<byte> name)
    {
        name = default;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return false;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                name = reader.ValueSpan;
                return true;
            }
        }

        return false;
    }

    public static bool NameEquals(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected) =>
        actual.SequenceEqual(expected);

    public static void SkipValue(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            return;
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
    }

    public static bool TryEnterObject(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            return false;
        if (reader.TokenType == JsonTokenType.StartObject)
            return true;
        if (reader.TokenType is JsonTokenType.StartArray)
            reader.Skip();
        return false;
    }

    public static string? ReadString(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            return null;
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
        return null;
    }

    public static long ReadInt64(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            return 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var n))
                    return n;
                if (reader.TryGetDouble(out var d))
                    return (long)d;
                return 0;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var df))
                    return (long)df;
                return 0;
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                reader.Skip();
                return 0;
            default:
                return 0;
        }
    }

    public static DateTimeOffset ReadTimestamp(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            return DateTimeOffset.UnixEpoch;
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return JsonUtil.ParseTimestamp(reader.GetString());
            case JsonTokenType.Number:
                long unix;
                if (reader.TryGetInt64(out unix))
                    return JsonUtil.FromUnixFlexible(unix);
                if (reader.TryGetDouble(out var d))
                    return JsonUtil.FromUnixFlexible((long)d);
                return DateTimeOffset.UnixEpoch;
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                reader.Skip();
                return DateTimeOffset.UnixEpoch;
            default:
                return DateTimeOffset.UnixEpoch;
        }
    }
}
