using System.Text.Json.Nodes;

namespace ConfigSync;

public static class SelfTest
{
    public static void Run()
    {
        int count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); count++; }
        var source = Engine.Parse("""{"Section":{"ApiUrl":"https://base/api","FrontendUrl":"https://front/path","New":true},"Array":[1,2],"Nullable":null,"Conflict":{"Child":1}}""");
        var target = Engine.Parse("""{"section":{"ApiUrl":"https://test/api","FrontendUrl":"https://testfront/path"},"Array":[8],"Nullable":null,"Conflict":false,"Extra":9}""");
        var findings = new List<Difference>();
        var merged = Engine.Merge(source, target, "Test", findings);
        Check(merged["section"]!["New"]!.GetValue<bool>(), "nested missing key");
        Check(merged["section"]!["ApiUrl"]!.GetValue<string>() == "https://test/api", "preserve existing URL");
        Check(merged["Array"]!.ToJsonString() == "[8]", "preserve environment array");
        Check(merged["Extra"]!.GetValue<int>() == 9, "preserve extras");
        Check(findings.Any(d => d.Status == "Type conflict"), "report conflict");
        Check(target["section"]!["New"] == null, "comparison does not mutate source");
        Engine.Apply(merged, [new UrlRule { Path = "/Section/ApiUrl", Role = "API" }], "https://override.example", "", "Test", findings);
        Check(merged["section"]!["ApiUrl"]!.GetValue<string>() == "https://override.example/api", "override keeps suffix");
        Check(Engine.Parse("{ //comment\n\"A\": 1, }")["A"]!.GetValue<int>() == 1, "JSON comments and trailing commas");
        bool invalid = false;
        try { Engine.Apply(merged, [new UrlRule { Path = "/Section/ApiUrl", Role = "API" }], "file:///bad", "", "Test", findings); } catch (InvalidDataException) { invalid = true; }
        Check(invalid, "reject invalid override");
        var moveSource = Engine.Parse("""{"عربي":{"a/b":{"~key":true}},"Other":2}""");
        var moveTarget = Engine.Parse("""{"عربي":{}}""");
        JsonText.Move(moveSource, moveTarget, "/عربي/a~1b");
        Check(JsonNode.DeepEquals(moveTarget["عربي"]!["a/b"], moveSource["عربي"]!["a/b"]), "move selected escaped path");
        Check(!moveTarget.ContainsKey("Other"), "selected move leaves other missing keys untouched");
        var highlightText = moveSource.ToJsonString(Engine.OutputOptions);
        var ranges = JsonText.Ranges(highlightText);
        var arabicRange = ranges["/عربي/a~1b/~0key"];
        Check(highlightText.Substring(arabicRange.Start, arabicRange.Length) == "\"~key\": true", "Unicode highlight character offsets");
        var parentRange = ranges["/عربي"];
        Check(highlightText.Substring(parentRange.Start, parentRange.Length).Contains("\"~key\": true"), "missing section highlights complete content");
        var orderBase = Engine.Parse("""{"First":{"A":0,"B":0},"Missing":1,"Last":0,"Array":[]}""");
        var orderTarget = Engine.Parse("""{"Extra1":true,"Last":42,"first":{"Extra":null,"B":2,"A":1},"Array":[{"Z":1,"A":2}],"Extra2":"keep"}""");
        var ordered = JsonText.MatchOrder(orderBase, orderTarget);
        Check(ordered.Select(p => p.Key).SequenceEqual(new[] { "first", "Last", "Array", "Extra1", "Extra2" }), "root follows baseline order preserving casing and extras");
        Check(((JsonObject)ordered["first"]!).Select(p => p.Key).SequenceEqual(new[] { "A", "B", "Extra" }), "nested keys follow baseline order");
        Check(JsonNode.DeepEquals(orderTarget, ordered), "reorder preserves every value including arrays and nulls");
        Check(!ordered.ContainsKey("Missing"), "reorder does not inject missing keys");
        Check(JsonText.MatchOrder(orderBase, ordered).ToJsonString() == ordered.ToJsonString(), "reordering is idempotent");
        Check(orderTarget.First().Key == "Extra1", "reordering leaves input unchanged");
        var replaceTarget = Engine.Parse("""{"https://old.test":"https://old.test/path?q=1","Nested":{"Url":"https://old.test/a https://old.test/b"},"Array":["https://old.test/x",{"Url":"https://old.test/y"}],"Number":42,"Null":null,"Other":"HTTPS://OLD.TEST"}""");
        var replacedPaths = JsonText.ReplaceValues(replaceTarget, "https://old.test", "https://new.test");
        Check(replacedPaths.Count == 4, "replacement reaches nested objects and arrays");
        Check(replaceTarget["https://old.test"]!.GetValue<string>() == "https://new.test/path?q=1", "replacement preserves property names and URL suffix");
        Check(replaceTarget["Nested"]!["Url"]!.GetValue<string>() == "https://new.test/a https://new.test/b", "replace all occurrences in value");
        Check(replaceTarget["Number"]!.GetValue<int>() == 42 && replaceTarget["Null"] == null && replaceTarget["Other"]!.GetValue<string>() == "HTTPS://OLD.TEST", "replacement preserves types and uses literal case-sensitive matching");
        Check(JsonText.ReplaceValues(replaceTarget, "does-not-exist", "anything").Count == 0, "unmatched replacement changes nothing");
        bool emptySearchRejected = false;
        try { JsonText.ReplaceValues(replaceTarget, "", "x"); } catch (InvalidOperationException) { emptySearchRejected = true; }
        Check(emptySearchRejected, "empty search rejected");
        string directory = Path.Combine(AppContext.BaseDirectory, "verification", DateTime.Now.ToString("yyyyMMddHHmmssfffffff"));
        Directory.CreateDirectory(directory);
        string fixture = Path.Combine(directory, "fixture.yml");
        string yaml = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: sample\ndata:\n  appsettings.json: |\n    {\n      \"Existing\": 7\n    }\n  other.txt: untouched\n";
        File.WriteAllText(fixture, yaml);
        var document = new ConfigDocument(fixture);
        string rendered = document.Render(Engine.Merge(Engine.Parse("{\"New\":true}"), document.Settings, "Test", []));
        Check(rendered.StartsWith(yaml[..yaml.IndexOf("    {")]), "preserve manifest prefix");
        Check(rendered.EndsWith("  other.txt: untouched\n"), "preserve sibling data");
        string renderedPath = Path.Combine(directory, "fixture.updated.yml");
        File.WriteAllText(renderedPath, rendered);
        Check(new ConfigDocument(renderedPath).Settings["New"]!.GetValue<bool>(), "roundtrip YAML block");
        string backup = OriginalFileSaver.Save(document, rendered);
        Check(File.ReadAllText(fixture) == rendered, "save updates the selected original file directly");
        Check(File.ReadAllText(backup) == yaml, "save retains exact original content in backup");
        bool staleRejected = false;
        try { OriginalFileSaver.Save(document, yaml); } catch (InvalidOperationException) { staleRejected = true; }
        Check(staleRejected && File.ReadAllText(fixture) == rendered, "stale save cannot overwrite externally changed file");
        var currentDocument = new ConfigDocument(fixture);
        bool invalidYamlRejected = false;
        try { OriginalFileSaver.Save(currentDocument, rendered + "broken: [unclosed"); } catch (YamlDotNet.Core.YamlException) { invalidYamlRejected = true; }
        Check(invalidYamlRejected && File.ReadAllText(fixture) == rendered, "invalid YAML cannot overwrite original");
        bool invalidJsonRejected = false;
        try { OriginalFileSaver.Save(currentDocument, rendered.Replace("true", "not-json")); } catch (System.Text.Json.JsonException) { invalidJsonRejected = true; }
        Check(invalidJsonRejected && File.ReadAllText(fixture) == rendered, "invalid embedded JSON cannot overwrite original");
        var realBasePath = @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\ConsumerVoice\ConsumerVoice.API\appsettings.json";
        if (File.Exists(realBasePath))
        {
            var baseline = Engine.Parse(File.ReadAllText(realBasePath));
            foreach (var (name, path) in new[] {
                ("prod", @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\DevOps_Prod\settings\be-appsettings-cm.yml"),
                ("test", @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\devops\settings\be-appsettings-cm.yml") })
            {
                var actual = new ConfigDocument(path);
                var changes = new List<Difference>();
                var output = Engine.Merge(baseline, actual.Settings, name, changes);
                string outputPath = Path.Combine(directory, name + ".updated.yml");
                File.WriteAllText(outputPath, actual.Render(output));
                Check(JsonNode.DeepEquals(output, new ConfigDocument(outputPath).Settings), name + " actual ConfigMap roundtrip");
                var second = new List<Difference>();
                Engine.Merge(baseline, output, name, second);
                Check(!second.Any(d => d.Status.StartsWith("Missing")), name + " all missing keys injected");
                File.WriteAllLines(Path.Combine(directory, name + ".report.txt"), changes.Select(d => $"{d.Status}: {d.Path} — {d.Action}"));
            }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification-result.txt"), $"PASS: {count} assertions\nArtifacts: {directory}");
    }
}



