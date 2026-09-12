using System.Text.Json;

namespace Mocha2021.Classes;

public static class JsonUtil
{
    public static object? Normalize(object? value)
    {
        if (value is not JsonElement el) return value;
        return el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => Normalize(p.Value)),
            JsonValueKind.Array => el.EnumerateArray().Select(e => Normalize(e)).ToList(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static Dictionary<string, object?> NormalizeDict(Dictionary<string, object?> dict) =>
        dict.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
}
