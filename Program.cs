using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;

namespace ConfigSync;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--ui-smoke"))
        {
            using var form = new MainForm();
            form.Show();
            form.VerifyComparisonUi();
            return;
        }
        Application.Run(new MainForm());
    }
}

public sealed class EnvironmentPanel : TabPage
{
    public TextBox FilePath { get; } = new() { Width = 790 };
    public TextBox SearchUrl { get; } = new() { Width = 330 };
    public TextBox Replacement { get; } = new() { Width = 330 };
    public Button ReplaceValues { get; } = new() { Text = "Replace matches", AutoSize = true, Enabled = false };
    public RichTextBox BaseContent { get; } = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White, HideSelection = false };
    public RichTextBox EnvironmentContent { get; } = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White, HideSelection = false };
    public ListBox Missing { get; } = new() { Dock = DockStyle.Fill, DisplayMember = "Path" };
    public Button MoveSelected { get; } = new() { Text = "Move selected →", AutoSize = true, Enabled = false };
    public Button MoveAll { get; } = new() { Text = "Move all missing →", AutoSize = true, Enabled = false };
    public Button SaveOriginal { get; } = new() { Text = "Save to original YAML", AutoSize = true, Enabled = false };
    public Button SaveEditor { get; } = new() { Text = "Save to original YAML", AutoSize = true, Enabled = false };
    public Button Reorder { get; } = new() { Text = "Match appsettings order", AutoSize = true, Enabled = false };
    public HashSet<string> Moved { get; } = new(StringComparer.OrdinalIgnoreCase);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public JsonObject? Working { get; set; }
    public TextBox Preview { get; } = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, ReadOnly = false, Font = new Font("Consolas", 10) };
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ConfigDocument? DraftDocument { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool YamlEdited { get; set; }
    bool updatingYaml;
    public void ShowYaml(string text)
    {
        updatingYaml = true;
        if (Preview.Text != text) Preview.Text = text;
        updatingYaml = false;
        YamlEdited = false;
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ConfigDocument? Document { get; set; }
    public EnvironmentPanel(string name, string path) : base(name)
    {
        FilePath.Text = path;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 145, Padding = new Padding(8) };
        var browse = new Button { Text = "Browse YAML", AutoSize = true };
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "YAML|*.yml;*.yaml" }; if (dialog.ShowDialog() == DialogResult.OK) FilePath.Text = dialog.FileName; };
        top.Controls.AddRange([new Label { Text = "ConfigMap", Width = 90 }, FilePath, browse]);
        top.SetFlowBreak(browse, true);
        top.Controls.AddRange([new Label { Text = "Search URL/text", Width = 115 }, SearchUrl, new Label { Text = "Replace with", Width = 110 }, Replacement]);
        top.SetFlowBreak(Replacement, true);
        top.Controls.AddRange([ReplaceValues, new Label { Text = "Literal, case-sensitive replacement in configuration string values. Changes are staged until Save.", AutoSize = true }]);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var preview = new TabPage("YAML editor"); preview.Controls.Add(Preview);
        var editorActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        editorActions.Controls.AddRange([SaveEditor, new Label { Text = "Edits save to this environment's original file with a backup.", AutoSize = true, Margin = new Padding(8, 10, 0, 0) }]);
        preview.Controls.Add(editorActions);
        Preview.TextChanged += (_, _) => { if (!updatingYaml) YamlEdited = true; };
        var content = new TabPage("Side-by-side comparison");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1080, 500), SplitterDistance = 530 };
        split.Panel1.Controls.Add(BaseContent);
        split.Panel1.Controls.Add(new Label { Text = "Base appsettings.json — yellow: missing in environment", Dock = DockStyle.Top, Height = 28 });
        split.Panel2.Controls.Add(EnvironmentContent);
        split.Panel2.Controls.Add(new Label { Text = "Environment YAML → data.appsettings.json — green: staged changes", Dock = DockStyle.Top, Height = 28 });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, AutoScroll = true };
        actions.Controls.AddRange([MoveSelected, MoveAll, SaveOriginal, Reorder]);
        var missingArea = new Panel { Dock = DockStyle.Bottom, Height = 135 };
        missingArea.Controls.Add(Missing); missingArea.Controls.Add(actions);
        content.Controls.Add(split); content.Controls.Add(missingArea);
        tabs.TabPages.AddRange([content, preview]);
        Controls.Add(tabs); Controls.Add(top);
    }
}

public sealed class MainForm : Form
{
    readonly TextBox baseline = new() { Width = 810, Text = @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\ConsumerVoice\ConsumerVoice.API\appsettings.json" };
    readonly EnvironmentPanel test = new("Test", @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\devops\settings\be-appsettings-cm.yml");
    readonly EnvironmentPanel prod = new("Production", @"D:\Expert- Projects\ConsumerVoice\ConsumerVoiceBE\DevOps_Prod\settings\be-appsettings-cm.yml");
    readonly DataGridView report = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, BackgroundColor = Color.White };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 30, Text = "Load files to begin. Originals are written only by Save." };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    JsonObject? source;
    string sourceText = "", sourcePath = "";
    List<Difference> differences = [];
    public MainForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "ConfigSync — JSON / Kubernetes ConfigMap"; Width = 1250; Height = 850; MinimumSize = new Size(1000, 650);
        Font = new Font("Segoe UI", 10);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 95, Padding = new Padding(8) };
        var browse = new Button { Text = "Base JSON…", AutoSize = true };
        browse.Click += (_, _) => Guard(() => { using var d = new OpenFileDialog { Filter = "JSON|*.json" }; if (d.ShowDialog() == DialogResult.OK) baseline.Text = d.FileName; });
        top.Controls.AddRange([browse, baseline]); top.SetFlowBreak(baseline, true);
        AddButton("Load / reload", LoadFiles);
        AddButton("Compare", Compare);
        AddButton("Export report CSV", Export);
        var reportTab = new TabPage("Comparison report"); reportTab.Controls.Add(report);
        tabs.TabPages.AddRange([test, prod, reportTab]);
        Controls.Add(tabs); Controls.Add(top); Controls.Add(status);
        foreach (var panel in new[] { test, prod })
        {
            panel.MoveSelected.Click += (_, _) => Guard(() => MoveMissing(panel, false));
            panel.MoveAll.Click += (_, _) => Guard(() => MoveMissing(panel, true));
            panel.Reorder.Click += (_, _) => Guard(() => Reorder(panel));
            panel.ReplaceValues.Click += (_, _) => Guard(() => ReplaceMatches(panel));
            panel.SaveOriginal.Click += (_, _) => Guard(Synchronize);
            panel.SaveEditor.Click += (_, _) => Guard(Synchronize);
            panel.Missing.SelectedIndexChanged += (_, _) => FocusMissing(panel);
        }
        void AddButton(string caption, Action action) { var button = new Button { Text = caption, AutoSize = true }; button.Click += (_, _) => Guard(action); top.Controls.Add(button); }
    }
    internal void VerifyComparisonUi()
    {
        LoadFiles();
        if (test.BaseContent.Text.Length == 0 || test.EnvironmentContent.Text.Length == 0 || test.Missing.Items.Count == 0)
            throw new Exception("Comparison content did not load.");
        var first = (Difference)test.Missing.Items[0];
        var range = JsonText.Ranges(test.BaseContent.Text)[first.Path];
        test.BaseContent.Select(range.Start, 1);
        if (test.BaseContent.SelectionBackColor != Color.FromArgb(255, 239, 153)) throw new Exception("Missing content not highlighted.");
        int before = test.Missing.Items.Count;
        test.MoveSelected.PerformClick();
        if (test.Missing.Items.Count != before - 1) throw new Exception("Move selected did not stage exactly one item.");
        range = JsonText.Ranges(test.EnvironmentContent.Text)[first.Path];
        test.EnvironmentContent.Select(range.Start, 1);
        if (test.EnvironmentContent.SelectionBackColor != Color.FromArgb(188, 239, 199)) throw new Exception("Moved content not highlighted.");
        test.MoveAll.PerformClick();
        if (test.Missing.Items.Count != 0) throw new Exception("Move all did not stage remaining items.");
        var beforeOrder = (JsonObject)test.Working!.DeepClone();
        test.Reorder.PerformClick();
        if (!JsonNode.DeepEquals(beforeOrder, test.Working)) throw new Exception("Reordering changed values.");
        if (Engine.Parse(test.EnvironmentContent.Text).ToJsonString() != JsonText.MatchOrder(source!, beforeOrder).ToJsonString()) throw new Exception("Reorder button did not refresh the content pane.");
        string prodBeforeReplace = prod.Working!.ToJsonString();
        string baseBeforeReplace = source!.ToJsonString();
        test.SearchUrl.Text = "https://consumervoicetestapi.expertapps.com.sa";
        test.Replacement.Text = "https://replacement.example";
        test.ReplaceValues.PerformClick();
        if (test.Working!["AppBaseUrl"]!.GetValue<string>() != "https://replacement.example") throw new Exception("Search/replace button did not update matching values.");
        if (!test.EnvironmentContent.Text.Contains("https://replacement.example") || !test.Preview.Text.Contains("https://replacement.example")) throw new Exception("Replacement previews were not refreshed.");
        if (prod.Working.ToJsonString() != prodBeforeReplace || source.ToJsonString() != baseBeforeReplace) throw new Exception("Replacement affected another environment or base.");
        string editedYaml = test.Preview.Text + Environment.NewLine + "# manually edited YAML" + Environment.NewLine;
        test.Preview.Text = editedYaml;
        if (test.Preview.ReadOnly || !test.YamlEdited) throw new Exception("YAML editor is not editable.");
        Compare();
        if (test.Preview.Text != editedYaml) throw new Exception("Compare discarded manual YAML edits.");
        test.Reorder.PerformClick();
        if (!test.Preview.Text.Contains("# manually edited YAML")) throw new Exception("Reorder lost manual YAML metadata/comments.");
        string validEditor = test.Preview.Text;
        test.Preview.Text += "broken: [unclosed";
        bool rejected = false;
        try { Compare(); } catch (YamlDotNet.Core.YamlException) { rejected = true; }
        if (!rejected || !test.Preview.Text.EndsWith("broken: [unclosed")) throw new Exception("Invalid YAML was not preserved for correction.");
        test.Preview.Text = validEditor;
        Compare();
        EnsureLoaded();
        string originalPath = test.Document!.Path;
        string originalText = test.Document.Original;
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "editor-verification", DateTime.Now.ToString("yyyyMMddHHmmssfffffff"));
        Directory.CreateDirectory(fixtureDirectory);
        string fixturePath = Path.Combine(fixtureDirectory, "manual.yml");
        string fixtureText = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: editor-test\ndata:\n  appsettings.json: |\n    {\n      \"Edited\": false\n    }\n";
        File.WriteAllText(fixturePath, fixtureText);
        test.FilePath.Text = fixturePath;
        test.Document = new ConfigDocument(fixturePath);
        test.DraftDocument = test.Document;
        test.Working = (JsonObject)test.Document.Settings.DeepClone();
        test.ShowYaml(fixtureText);
        string manualText = fixtureText.Replace("\"Edited\": false", "\"Edited\": true") + "# preserve my comment\n";
        test.Preview.Text = manualText;
        Synchronize();
        if (File.ReadAllText(fixturePath) != manualText || test.Document.Settings["Edited"]!.GetValue<bool>() != true)
            throw new Exception("Save did not write the manually edited YAML to its original path.");
        string backupPath = Directory.GetFiles(fixtureDirectory, "manual.yml.*.bak").Single();
        if (File.ReadAllText(backupPath) != fixtureText || File.ReadAllText(originalPath) != originalText)
            throw new Exception("Manual save failed to preserve the backup or touched a real input.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-verification-result.txt"), "PASS: comparison, move, reorder and replacement; editable YAML preserved through Compare; invalid YAML rejected without discarding text; manual Save wrote an isolated original fixture and retained its backup; real input files unchanged.");
    }
    void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ConfigSync", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    void LoadFiles()
    {
        var basePath = Path.GetFullPath(baseline.Text);
        var testPath = Path.GetFullPath(test.FilePath.Text);
        var prodPath = Path.GetFullPath(prod.FilePath.Text);
        if (new[] { basePath, testPath, prodPath }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3) throw new InvalidDataException("Base, test and production must use distinct files.");
        var text = File.ReadAllText(basePath);
        var parsed = Engine.Parse(text);
        var testDoc = new ConfigDocument(testPath);
        var prodDoc = new ConfigDocument(prodPath);
        source = parsed; sourceText = text; sourcePath = basePath;
        Populate(test, testDoc); Populate(prod, prodDoc);
        Compare();
    }
    void Populate(EnvironmentPanel panel, ConfigDocument document)
    {
        panel.Document = document;
        panel.SaveOriginal.Enabled = panel.SaveEditor.Enabled = true;
        panel.DraftDocument = document;
        panel.ShowYaml(document.Original);
        panel.Working = (JsonObject)document.Settings.DeepClone(); panel.Moved.Clear(); panel.Reorder.Enabled = true;
        panel.SearchUrl.Clear(); panel.Replacement.Clear(); panel.ReplaceValues.Enabled = true;
    }
    void EnsureLoaded()
    {
        if (source == null || test.Document == null || prod.Document == null) throw new InvalidOperationException("Click Load / reload first.");
        if (!Path.GetFullPath(baseline.Text).Equals(sourcePath, StringComparison.OrdinalIgnoreCase) || File.ReadAllText(sourcePath) != sourceText) throw new InvalidOperationException("Base file changed. Reload before continuing.");
        foreach (var panel in new[] { test, prod })
            if (!Path.GetFullPath(panel.FilePath.Text).Equals(panel.Document!.Path, StringComparison.OrdinalIgnoreCase) || File.ReadAllText(panel.Document.Path) != panel.Document.Original)
                throw new InvalidOperationException(panel.Text + " file changed. Reload before continuing.");
    }
    void Compare()
    {
        EnsureLoaded(); ReadYamlEdits(); differences = [];
        foreach (var panel in new[] { test, prod })
        {
            var findings = new List<Difference>();
            Engine.Merge(source!, panel.Working!, panel.Text, findings);
            differences.AddRange(findings);
            panel.BaseContent.Text = source!.ToJsonString(Engine.OutputOptions);
            panel.EnvironmentContent.Text = panel.Working!.ToJsonString(Engine.OutputOptions);
            panel.Missing.DataSource = findings.Where(d => d.Status.StartsWith("Missing")).ToArray();
            panel.MoveSelected.Enabled = panel.MoveAll.Enabled = panel.Missing.Items.Count > 0;
            HighlightMissing(panel.BaseContent, findings.Where(d => d.Status.StartsWith("Missing")).Select(d => d.Path), Color.FromArgb(255, 239, 153));
            HighlightMissing(panel.EnvironmentContent, panel.Moved, Color.FromArgb(188, 239, 199));
            panel.ShowYaml(panel.Working.ToJsonString() == panel.DraftDocument!.Settings.ToJsonString() ? panel.DraftDocument.Original : panel.DraftDocument.Render(panel.Working));
        }
        report.DataSource = differences.ToArray();
        status.Text = "Compare highlights missing content in yellow. Move selected / all → stages additions in green. Save updates the selected original YAML with staged changes.";
    }
    static void HighlightMissing(RichTextBox box, IEnumerable<string> paths, Color color)
    {
        var ranges = JsonText.Ranges(box.Text);
        foreach (var path in paths)
        {
            string location = path;
            while (!ranges.ContainsKey(location) && location.Contains('/')) location = location[..location.LastIndexOf('/')];
            if (ranges.TryGetValue(location, out var range)) { box.Select(range.Start, range.Length); box.SelectionBackColor = color; }
        }
        box.Select(0, 0);
    }
    void FocusMissing(EnvironmentPanel panel)
    {
        if (panel.Missing.SelectedItem is not Difference item) return;
        var ranges = JsonText.Ranges(panel.BaseContent.Text);
        if (ranges.TryGetValue(item.Path, out var range))
        {
            panel.BaseContent.Select(range.Start, range.Length); panel.BaseContent.ScrollToCaret();
        }
        var right = JsonText.Ranges(panel.EnvironmentContent.Text);
        string parent = item.Path;
        while (parent.Contains('/'))
        {
            parent = parent[..parent.LastIndexOf('/')];
            if (right.TryGetValue(parent, out var existing))
            {
                panel.EnvironmentContent.Select(existing.Start, 0); panel.EnvironmentContent.ScrollToCaret(); return;
            }
        }
        panel.EnvironmentContent.Select(0, 0); panel.EnvironmentContent.ScrollToCaret();
    }
    void MoveMissing(EnvironmentPanel panel, bool all)
    {
        EnsureLoaded();
        string? selectedPath = (panel.Missing.SelectedItem as Difference)?.Path;
        Compare();
        if (!all) panel.Missing.SelectedItem = panel.Missing.Items.Cast<Difference>().FirstOrDefault(d => d.Path == selectedPath);
        var selected = all ? panel.Missing.Items.Cast<Difference>().ToArray() : panel.Missing.SelectedItem is Difference item ? new[] { item } : [];
        if (selected.Length == 0) throw new InvalidOperationException("Click Compare and select a missing item first.");
        foreach (var entry in selected)
        {
            JsonText.Move(source!, panel.Working!, entry.Path);
            panel.Moved.Add(entry.Path);
        }
        Compare();
        var range = JsonText.Ranges(panel.EnvironmentContent.Text).GetValueOrDefault(selected[0].Path);
        panel.EnvironmentContent.Select(range.Start, range.Length); panel.EnvironmentContent.ScrollToCaret();
        status.Text = $"Moved {selected.Length} missing item(s) into {panel.Text} preview. Click Save to original YAML to save.";
    }
    void ReplaceMatches(EnvironmentPanel panel)
    {
        EnsureLoaded(); ReadYamlEdits();
        var candidate = (JsonObject)panel.Working!.DeepClone();
        var changed = JsonText.ReplaceValues(candidate, panel.SearchUrl.Text, panel.Replacement.Text);
        panel.Working = candidate;
        foreach (var path in changed) panel.Moved.Add(path);
        Compare();
        status.Text = $"Replaced matches in {changed.Count} value(s) in {panel.Text}. Changes are green; click Save to original YAML to save. Repeat with another search URL if needed.";
    }
    void Reorder(EnvironmentPanel panel)
    {
        EnsureLoaded(); ReadYamlEdits();
        panel.Working = JsonText.MatchOrder(source!, panel.Working!);
        Compare();
        status.Text = panel.Text + " sections and nested keys now follow appsettings order. Environment-only keys remain at the end. Click Save to original YAML to save.";
    }
    void Synchronize()
    {
        if (tabs.SelectedTab is not EnvironmentPanel panel) throw new InvalidOperationException("Select the Test or Production tab to save.");
        Compare();
        if (panel.Preview.Text == panel.Document!.Original) { status.Text = "No changes to save."; return; }
        EnsureLoaded();
        string backup = OriginalFileSaver.Save(panel.Document, panel.Preview.Text);
        panel.Document = new ConfigDocument(panel.Document.Path);
        panel.DraftDocument = panel.Document;
        panel.Working = (JsonObject)panel.Document.Settings.DeepClone(); panel.Moved.Clear();
        Compare(); status.Text = "Saved " + panel.Document.Path + " | Backup: " + backup;
    }
    void ReadYamlEdits()
    {
        var edits = new[] { test, prod }.Where(p => p.YamlEdited)
            .Select(p => (Panel: p, Draft: new ConfigDocument(p.Document!.Path, p.Preview.Text))).ToArray();
        foreach (var edit in edits)
        {
            edit.Panel.DraftDocument = edit.Draft;
            edit.Panel.Working = (JsonObject)edit.Draft.Settings.DeepClone();
            edit.Panel.YamlEdited = false;
            edit.Panel.Moved.Clear();
        }
    }
    void Export()
    {
        Compare();
        using var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "config-comparison.csv" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var protectedPaths = new[] { sourcePath, test.Document!.Path, prod.Document!.Path };
        if (protectedPaths.Contains(Path.GetFullPath(dialog.FileName), StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Report cannot overwrite a configuration file.");
        static string Csv(string s) => "\"" + (s.StartsWith('=') || s.StartsWith('+') || s.StartsWith('-') || s.StartsWith('@') ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        File.WriteAllLines(dialog.FileName, new[] { "Environment,Path,Status,Action" }.Concat(differences.Select(d => string.Join(",", new[] { d.Environment, d.Path, d.Status, d.Action }.Select(Csv)))), new UTF8Encoding(true));
        status.Text = "Report exported (paths and actions only; no configuration values).";
    }
}









