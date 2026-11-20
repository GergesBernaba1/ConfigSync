using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace ConfigSync;

public record Difference(string Environment, string Path, string Status, string Action);
public sealed class UrlRule
{
    public string Path { get; set; } = "";
    public string Role { get; set; } = "Preserve";
}

public sealed class ConfigDocument
{
    public string Path { get; }
    public string Original { get; }
    public JsonObject Settings { get; }
    readonly int start, end;
    readonly string indent, newline;
    public ConfigDocument(string path) : this(path, File.ReadAllText(path)) { }
    public ConfigDocument(string path, string text)
    {
        Path = System.IO.Path.GetFullPath(path);
        Original = text;
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root
            || !root.Children.TryGetValue(new YamlScalarNode("kind"), out var kind) || kind is not YamlScalarNode { Value: "ConfigMap" }
            || !root.Children.TryGetValue(new YamlScalarNode("data"), out var data) || data is not YamlMappingNode mapping
            || !mapping.Children.TryGetValue(new YamlScalarNode("appsettings.json"), out var settings) || settings is not YamlScalarNode embedded)
            throw new InvalidDataException("Expected one Kubernetes ConfigMap with data.appsettings.json containing JSON.");
        newline = Original.Contains("\r\n") ? "\r\n" : "\n";
        var header = Regex.Match(Original, @"(?m)^(?<indent> +)appsettings\.json:[ \t]*\|[+-]?[ \t]*(?:#[^\r\n]*)?\r?$\n");
        if (!header.Success) throw new InvalidDataException("Expected a ConfigMap with data.appsettings.json as a YAML literal block (|).");
        var prefix = Original[..header.Index];
        if (!Regex.IsMatch(prefix, @"(?m)^data:[ \t]*\r?$") || !Regex.IsMatch(prefix, @"(?m)^kind:[ \t]*ConfigMap[ \t]*\r?$"))
            throw new InvalidDataException("Expected a Kubernetes ConfigMap data block.");
        if (Regex.Matches(Original, @"(?m)^ +appsettings\.json:").Count != 1 || Regex.IsMatch(Original, @"(?m)^---[ \t]*\r?$"))
            throw new InvalidDataException("Select a single ConfigMap document with one appsettings.json block.");
        start = header.Index + header.Length;
        int keyIndent = header.Groups["indent"].Length;
        var lines = Regex.Matches(Original[start..], @"[^\n]*(?:\n|$)").Cast<Match>().Where(m => m.Length > 0).ToList();
        var content = new StringBuilder();
        int? contentIndent = null;
        end = start;
        foreach (var line in lines)
        {
            string value = line.Value.TrimEnd('\r', '\n');
            int spaces = value.TakeWhile(c => c == ' ').Count();
            if (!string.IsNullOrWhiteSpace(value) && spaces <= keyIndent) break;
            if (!string.IsNullOrWhiteSpace(value)) contentIndent ??= spaces;
            if (!string.IsNullOrWhiteSpace(value) && spaces < contentIndent) throw new InvalidDataException("Invalid block indentation.");
            content.AppendLine(string.IsNullOrWhiteSpace(value) ? "" : value[(contentIndent ?? 0)..]);
            end = start + line.Index + line.Length;
        }
        indent = new string(' ', contentIndent ?? keyIndent + 2);
        Settings = Engine.Parse(content.ToString());
        if (!JsonNode.DeepEquals(Settings, Engine.Parse(embedded.Value ?? "")))
            throw new InvalidDataException("The appsettings JSON block does not match the YAML data value.");
    }
    public string Render(JsonObject settings)
    {
        string json = settings.ToJsonString(Engine.OutputOptions).Replace("\r\n", "\n");
        return Original[..start] + string.Join(newline, json.Split('\n').Select(l => indent + l)) + newline + Original[end..];
    }
}

public static class Engine
{
    public static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static JsonObject Parse(string text)
    {
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (node is not JsonObject obj) throw new InvalidDataException("Configuration root must be an object.");
        Validate(obj);
        return obj;
    }
    static void Validate(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in obj)
            {
                if (!keys.Add(pair.Key)) throw new InvalidDataException("Ambiguous duplicate configuration key: " + pair.Key);
                Validate(pair.Value);
            }
        }
        else if (node is JsonArray array) foreach (var child in array) Validate(child);
    }
    public static Dictionary<string, string> Strings(JsonObject obj)
    {
        var result = new Dictionary<string, string>();
        Walk(obj, "");
        return result;
        void Walk(JsonObject current, string path)
        {
            foreach (var pair in current)
            {
                string p = path + "/" + Escape(pair.Key);
                if (pair.Value is JsonObject child) Walk(child, p);
                else if (pair.Value is JsonValue value && value.TryGetValue<string>(out var s)) result[p] = s;
            }
        }
    }
    public static string Escape(string s) => s.Replace("~", "~0").Replace("/", "~1");
    public static JsonObject Merge(JsonObject source, JsonObject target, string environment, List<Difference> report)
    {
        var result = (JsonObject)target.DeepClone();
        Walk(source, result, "");
        return result;
        void Walk(JsonObject baseline, JsonObject dest, string path)
        {
            foreach (var pair in baseline)
            {
                string p = path + "/" + Escape(pair.Key);
                string? key = dest.Select(k => k.Key).FirstOrDefault(k => k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (key == null)
                {
                    dest[pair.Key] = pair.Value?.DeepClone();
                    report.Add(new(environment, p, pair.Value is JsonObject ? "Missing section" : "Missing key", "Add from base"));
                }
                else if (pair.Value is JsonObject child && dest[key] is JsonObject existing) Walk(child, existing, p);
                else if (!JsonNode.DeepEquals(pair.Value, dest[key]))
                {
                    bool conflict = Kind(pair.Value) != Kind(dest[key]);
                    report.Add(new(environment, p, conflict ? "Type conflict" : "Different value", "Preserve environment value"));
                }
            }
            foreach (var pair in dest.Where(k => !baseline.Any(b => b.Key.Equals(k.Key, StringComparison.OrdinalIgnoreCase))))
                report.Add(new(environment, path + "/" + Escape(pair.Key), "Environment only", "Preserve"));
        }
    }
    static JsonValueKind Kind(JsonNode? node) => node?.GetValueKind() ?? JsonValueKind.Null;
    public static void Apply(JsonObject target, IEnumerable<UrlRule> rules, string api, string frontend, string environment, List<Difference> report)
    {
        foreach (var rule in rules.Where(r => r.Role != "Preserve"))
        {
            string input = rule.Role == "API" ? api : frontend;
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (!Uri.TryCreate(input, UriKind.Absolute, out var root) || (root.Scheme != "https" && root.Scheme != "http") || root.AbsolutePath != "/" || root.Query != "" || root.Fragment != "" || root.UserInfo != "")
                throw new InvalidDataException("URL overrides must be HTTP(S) origins, e.g. https://api.example.com (no path, query or credentials).");
            var parts = rule.Path.Split('/').Skip(1).Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();
            JsonObject parent = target;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var key = parent.Select(k => k.Key).FirstOrDefault(k => k.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (key == null || parent[key] is not JsonObject next) throw new InvalidDataException("Cannot override conflicting path " + rule.Path);
                parent = next;
            }
            string leaf = parent.Select(k => k.Key).First(k => k.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
            if (parent[leaf] is not JsonValue value || !value.TryGetValue<string>(out var old)) throw new InvalidDataException("URL path is not a string: " + rule.Path);
            string updated = Uri.TryCreate(old, UriKind.Absolute, out var previous) ? root.GetLeftPart(UriPartial.Authority) + old[previous.GetLeftPart(UriPartial.Authority).Length..] : input.TrimEnd('/');
            if (old != updated)
            {
                parent[leaf] = updated;
                report.Add(new(environment, rule.Path, "URL override", "Apply on Synchronize"));
            }
        }
    }
}
