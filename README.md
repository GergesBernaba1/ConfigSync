# ConfigSync

Standalone .NET 10 Windows Forms configuration comparison tool.

## Open the current version

`bin\SaveActions\ConfigSync.exe`

The application icon is embedded in the executable and shown in the window/taskbar.

## Workflow

1. Click **Load / reload** to load base appsettings.json and the Test / Production Kubernetes ConfigMaps.
2. **Compare** shows base JSON and the selected environment's embedded JSON side by side. Missing sections/keys are yellow on the left.
3. Select a missing path and click **Move selected →**, or **Move all missing →**. These additions are staged and shown green on the right.
4. **Match appsettings order** stages section and nested-key ordering matching the base. Environment-only keys stay at the end; arrays retain their order.
5. To change URLs, enter the current URL (or literal text) in **Search URL/text**, enter its replacement in **Replace with**, then click **Replace matches**. Repeat for additional URLs. Each environment has its own controls. This replaces the former API/frontend origin controls and URL role mapping.
6. Inspect the right text area or full **YAML editor**, then click **Save to original YAML** to save staged changes with a timestamped backup. Clicking Save writes directly to the selected original YAML. A timestamped backup is created first; the file is locked against other writers during save and the written content is verified.

## Search and replacement

Literal, case-sensitive replacement of every occurrence in configuration string values, including nested objects and array items. Property names, numeric/boolean/null values, base JSON, the other environment, and outer Kubernetes metadata are preserved. URL paths remain when replacing only an origin; searching for a complete URL replaces that complete matching text. An empty search is rejected; an empty replacement is allowed. No match produces zero changed values. Only content already in the environment preview is affected, so move missing settings before replacing their URLs. Matched properties are green; matching array entries highlight their containing array.

## File behavior

Supports the supplied single-document Kubernetes ConfigMap with JSON embedded in `data.appsettings.json: |`. YAML metadata and sibling content outside that block are retained; the embedded JSON is reformatted and its comments are not retained. JSON comments and trailing commas are accepted on input. Existing environment values and arrays are preserved by missing-key synchronization; type conflicts require manual review. CSV exports list paths, statuses and actions without configuration values. All processing is local. No database or file deletion operations are performed.

`UpdatedYaml` contains previously generated missing-key synchronization copies. They do not include newly staged UI changes; Save to original YAML saves those to the file selected in the environment tab.

## Build and verify

Build: `dotnet build ConfigSync.csproj -p:OutputPath=bin\SaveActions\`

Engine checks: `dotnet bin\SaveActions\ConfigSync.dll --self-test`

UI smoke check: `dotnet bin\SaveActions\ConfigSync.dll --ui-smoke`

Latest validation: zero build warnings/errors; 37 engine assertions passed. UI smoke checks verified both panes, highlights, Move buttons, ordering, replacement preview updates, and preservation of original files and other environments.


## Direct YAML editing
Open the YAML editor tab under Test or Production, edit the complete YAML content, then click Save to original YAML. YamlDotNet validates the YAML document and ConfigSync validates the embedded appsettings JSON before any file write. Compare retains manual text; Move, Replace and Match order use the edited document and preserve its outer YAML content. Invalid edits remain in the editor for correction. Saving writes the selected original file directly, creates a backup first, and verifies the saved text. The side-by-side configuration panes remain comparison views. The packaged YamlDotNet dependency is required alongside the executable.
Latest verification: 37 engine checks passed; UI smoke test verified manual edits, validation, exact fixture saving and backup retention without modifying real inputs.


Save to original YAML is now beside the Move buttons in each environment's comparison view, with another Save button above its YAML editor. The top toolbar contains Load, Compare and Export.

