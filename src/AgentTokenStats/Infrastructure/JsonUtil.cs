using System.Globalization;
using System.Text.Json;

namespace AgentTokenStats.Infrastructure;

public static class JsonUtil
{
    public static bool TryParse(string? json, out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetProperty(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    public static bool TryGetString(JsonElement el, string name, out string? value)
    {
        value = null;
        if (!TryGetProperty(el, name, out var p))
            return false;
        if (p.ValueKind != JsonValueKind.String)
            return false;
        value = p.GetString();
        return !string.IsNullOrEmpty(value);
    }

    public static string? GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetString(el, name, out var value))
                return value;
        }

        return null;
    }

    public static long GetInt64(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(el, name, out var p))
                return CoerceInt64(p);
        }

        return 0;
    }

    public static bool GetBool(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(el, name, out var p))
                continue;
            if (p.ValueKind == JsonValueKind.True)
                return true;
            if (p.ValueKind == JsonValueKind.False)
                return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n))
                return n != 0;
            if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b))
                return b;
        }

        return false;
    }

    public static DateTimeOffset ParseTimestamp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(el, name, out var p))
                continue;
            var parsed = ParseTimestamp(p);
            if (parsed != DateTimeOffset.UnixEpoch)
                return parsed;
        }

        return DateTimeOffset.UnixEpoch;
    }

    public static DateTimeOffset ParseTimestamp(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => ParseTimestamp(el.GetString()),
            JsonValueKind.Number => FromUnixFlexible(CoerceInt64(el)),
            _ => DateTimeOffset.UnixEpoch
        };
    }

    public static DateTimeOffset ParseTimestamp(string? text, DateTimeOffset? fallback = null)
    {
        var def = fallback ?? DateTimeOffset.UnixEpoch;
        if (string.IsNullOrWhiteSpace(text))
            return def;
        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
            return dto;
        return def;
    }

    public static DateTimeOffset FromUnixFlexible(long value)
    {
        if (value <= 0)
            return DateTimeOffset.UnixEpoch;
        try
        {
            return value > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    public static long CoerceInt64(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var n))
                    return n;
                if (el.TryGetDouble(out var d))
                    return (long)d;
                return 0;
            case JsonValueKind.String:
                var s = el.GetString();
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var df))
                    return (long)df;
                return 0;
            default:
                return 0;
        }
    }
}
