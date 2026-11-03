namespace Tracto.Designer.Core.Templates;

using Tracto.Designer.Core.Scripting;

public sealed class BuiltInTemplateLibrary : ITemplateLibrary
{
    private static readonly IReadOnlyList<ScriptTemplate> _all = BuildTemplates();

    public IReadOnlyList<ScriptTemplate> GetAll() => _all;

    public IReadOnlyList<ScriptTemplate> GetByCategory(TemplateCategory cat) =>
        [.._all.Where(t => t.Category == cat)];

    public IReadOnlyList<ScriptTemplate> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _all;
        var q = query.Trim().ToLowerInvariant();
        return [.._all.Where(t =>
            t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Tags.Any(tag => tag.Contains(q, StringComparison.OrdinalIgnoreCase)))];
    }

    private static List<ScriptTemplate> BuildTemplates() =>
    [
        MakeTemplate("login-form", "Login Form", "Username, password, and submit", TemplateCategory.Login,
            ["login", "auth", "credentials"],
            () => MakeScript("login-form", "Login Form", "desktop",
                ("click", "Click on Username field", new() { ["target"] = "username" }),
                ("type",  "Type username",            new() { ["target"] = "username", ["value"] = "{{username}}" }),
                ("click", "Click on Password field",  new() { ["target"] = "password" }),
                ("type",  "Type password",            new() { ["target"] = "password", ["value"] = "{{password}}" }),
                ("click", "Click Submit button",      new() { ["target"] = "submit-btn" }))),

        MakeTemplate("web-navigation", "Web Navigation", "Navigate to URL and verify page load", TemplateCategory.Navigation,
            ["navigate", "url", "page"],
            () => MakeScript("web-navigation", "Web Navigation", "browser",
                ("navigate", "Open the target URL",          new() { ["url"] = "{{targetUrl}}" }),
                ("wait",     "Wait for the page heading",    new() { ["role"] = "heading", ["name"] = "{{headingText}}" }),
                ("assert",   "Verify the page URL",          new() { ["kind"] = "urlEquals", ["expected"] = "{{targetUrl}}" }))),

        MakeTemplate("data-entry-form", "Data Entry Form", "Fill multiple fields and submit", TemplateCategory.DataEntry,
            ["form", "input", "data"],
            () => MakeScript("data-entry-form", "Data Entry Form", "desktop",
                ("click", "Click on first field",   new() { ["target"] = "field1" }),
                ("type",  "Enter value in field 1", new() { ["target"] = "field1", ["value"] = "{{field1Value}}" }),
                ("click", "Click on second field",  new() { ["target"] = "field2" }),
                ("type",  "Enter value in field 2", new() { ["target"] = "field2", ["value"] = "{{field2Value}}" }),
                ("click", "Submit the form",         new() { ["target"] = "submit-btn" }))),

        MakeTemplate("search-and-verify", "Search and Verify", "Search for item and verify results", TemplateCategory.Verification,
            ["search", "verify", "results"],
            () => MakeScript("search-and-verify", "Search and Verify", "browser",
                ("navigate", "Open the search page",          new() { ["url"] = "{{searchUrl}}" }),
                ("fill",     "Type the search text",          new() { ["role"] = "searchbox", ["name"] = "{{searchBoxName}}", ["value"] = "{{searchQuery}}" }),
                ("delay",    "Let the page attach handlers",  new() { ["ms"] = "500" }),
                ("press",    "Submit the search",             new() { ["role"] = "searchbox", ["name"] = "{{searchBoxName}}", ["key"] = "Enter" }),
                ("wait",     "Wait for the results",          new() { ["css"] = "{{resultsCss}}", ["timeoutMs"] = "15000" }))),

        MakeTemplate("file-download", "File Download", "Click download and verify file saved", TemplateCategory.FileOperation,
            ["download", "file", "save"],
            () => MakeScript("file-download", "File Download", "browser",
                ("navigate", "Open the page with the download", new() { ["url"] = "{{pageUrl}}" }),
                ("wait",     "Wait for the download link",      new() { ["role"] = "link", ["name"] = "{{linkText}}" }),
                ("click",    "Click the download link",         new() { ["role"] = "link", ["name"] = "{{linkText}}" }))),

        MakeTemplate("file-upload", "File Upload", "Choose file and upload", TemplateCategory.FileOperation,
            ["upload", "file", "attach"],
            () => MakeScript("file-upload", "File Upload", "desktop",
                ("click", "Click Choose File button", new() { ["target"] = "file-input" }),
                ("type",  "Enter file path",          new() { ["target"] = "file-dialog", ["value"] = "{{filePath}}" }),
                ("click", "Confirm file selection",   new() { ["target"] = "open-btn" }),
                ("click", "Click Upload button",      new() { ["target"] = "upload-btn" }))),

        MakeTemplate("table-extraction", "Data Table Extraction", "Read and verify table row data", TemplateCategory.DataEntry,
            ["table", "extract", "read", "data"],
            () => MakeScript("table-extraction", "Data Table Extraction", "browser",
                ("navigate", "Open the page with the table",   new() { ["url"] = "{{pageUrl}}" }),
                ("wait",     "Wait for the table",             new() { ["css"] = "{{tableCss}}" }),
                ("getText",  "Read the first row",             new() { ["css"] = "{{tableCss}} tbody tr >> nth=0", ["storeAs"] = "row" }))),

        MakeTemplate("logout", "Logout", "Click logout and verify session ended", TemplateCategory.General,
            ["logout", "signout", "session"],
            () => MakeScript("logout", "Logout", "desktop",
                ("click", "Click Logout button", new() { ["target"] = "logout-btn" }),
                ("focus", "Verify logged out",   new() { ["target"] = "login-form" })))
    ];

    private static ScriptTemplate MakeTemplate(string id, string name, string desc,
        TemplateCategory cat, string[] tags, Func<Script> create) =>
        new(id, name, desc, cat, tags, create);

    private static Script MakeScript(string id, string name, string runtime,
        params (string action, string desc, Dictionary<string, string?> parameters)[] steps)
    {
        var s = new Script { Id = id, Name = name, Version = "1.0.0", TargetRuntime = runtime };
        for (int i = 0; i < steps.Length; i++)
        {
            var (action, desc, prms) = steps[i];
            var step = new ScriptStep { StepNumber = i + 1, Description = desc, Action = action };
            foreach (var (k, v) in prms) step.Parameters[k] = v;
            s.Steps.Add(step);
        }
        return s;
    }
}
