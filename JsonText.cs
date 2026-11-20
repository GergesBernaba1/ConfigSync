using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfigSync;

public static class JsonText
{
    public static Dictionary<string, (int Start, int Length)> Ranges(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        var result = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        reader.Read();
        ReadObject(ref reader, "", bytes, result);
        return result;
    }
    static void ReadObject(ref Utf8JsonReader reader, string path, byte[] bytes, Dictionary<string, (int, int)> result)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string key = reader.GetString()!;
            int start = (int)reader.TokenStartIndex;
            string current = path + "/" + Engine.Escape(key);
            reader.Read();
            if (reader.TokenType == JsonTokenType.StartObject) ReadObject(ref reader, current, bytes, result);
            else reader.Skip();
            int end = (int)reader.BytesConsumed;
            result[current] = (Encoding.UTF8.GetCharCount(bytes.AsSpan(0, start)), Encoding.UTF8.GetCharCount(bytes.AsSpan(start, end - start)));
        }
    }
    public static List<string> ReplaceValues(JsonObject target, string search, string replacement)
    {
        if (string.IsNullOrWhiteSpace(search)) throw new InvalidOperationException("Enter a search URL or text first.");
        var changed = new List<string>();
        Walk(target, "");
        return changed;
        void Walk(JsonNode? node, string path)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(p => p.Key).ToArray())
                    Visit(obj[key], path + "/" + Engine.Escape(key), value => obj[key] = value);
            }
            else if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++)
                {
                    int index = i;
                    Visit(array[i], path + "/" + i, value => array[index] = value);
                }
        }
        void Visit(JsonNode? node, string path, Action<string> set)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                string updated = text.Replace(search, replacement, StringComparison.Ordinal);
                if (updated != text) { set(updated); changed.Add(path); }
            }
            else Walk(node, path);
        }
    }
    public static JsonObject MatchOrder(JsonObject source, JsonObject target)
    {
        var ordered = new JsonObject();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
        {
            string? key = target.Select(p => p.Key).FirstOrDefault(k => k.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            matched.Add(key);
            ordered[key] = entry.Value is JsonObject baseline && target[key] is JsonObject environment
                ? MatchOrder(baseline, environment)
                : target[key]?.DeepClone();
        }
        foreach (var entry in target.Where(p => !matched.Contains(p.Key))) ordered[entry.Key] = entry.Value?.DeepClone();
        return ordered;
    }
    public static void Move(JsonObject source, JsonObject target, string path)
    {
        var parts = path.Split('/').Skip(1).Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();
        JsonObject from = source, to = target;
        for (int i = 0; i < parts.Length; i++)
        {
            string fromKey = from.Select(p => p.Key).First(k => k.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            string? toKey = to.Select(p => p.Key).FirstOrDefault(k => k.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (i == parts.Length - 1)
            {
                if (toKey != null) throw new InvalidOperationException("This key already exists. Compare again.");
                to[fromKey] = from[fromKey]?.DeepClone();
            }
            else
            {
                if (from[fromKey] is not JsonObject nextFrom || toKey == null || to[toKey] is not JsonObject nextTo)
                    throw new InvalidOperationException("Move the missing parent section first.");
                from = nextFrom; to = nextTo;
            }
        }
    }
}


