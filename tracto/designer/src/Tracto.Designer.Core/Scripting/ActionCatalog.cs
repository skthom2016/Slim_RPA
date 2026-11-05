namespace Tracto.Designer.Core.Scripting;

/// <summary>
/// What one action takes and what it produces. <see cref="Outputs"/> is what makes a step's
/// result reachable from a later step: a step with a <c>storeAs</c> exposes each of these as
/// <c>${storeAs.field}</c>, and a later segment reads them as <c>${param.storeAs.field}</c>.
/// </summary>
public sealed record ActionDefinition(
    string Name,
    string Runtime,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Optional,
    IReadOnlyList<string> Outputs,
    bool NeedsElement = false)
{
    /// <summary>Every parameter the action understands, required ones first.</summary>
    public IEnumerable<string> AllParameters => Required.Concat(Optional);

    public bool ProducesData => Outputs.Count > 0;
}

/// <summary>
/// The actions each runtime understands. The catalogue is advisory: a runtime may accept more
/// than this lists, so the editor suggests these without refusing anything else.
/// </summary>
public static class ActionCatalog
{
    /// <summary>Any one of these identifies the element a browser action works on.</summary>
    public static readonly IReadOnlyList<string> BrowserTargets =
        ["role", "name", "label", "placeholder", "testId", "text", "css", "xpath"];

    /// <summary>
    /// The parameters that actually pick an element. <c>name</c> is missing on purpose: it
    /// qualifies <c>role</c> rather than standing on its own, so a step naming both still names
    /// the element only once.
    /// </summary>
    public static readonly IReadOnlyList<string> ElementSelectors =
        ["role", "label", "placeholder", "testId", "text", "css", "xpath"];

    /// <summary>Parameters whose value has to be a whole number of milliseconds.</summary>
    public static readonly IReadOnlyList<string> NumericParameters = ["ms", "timeoutMs"];

    /// <summary>The longest delay the desktop runtime accepts.</summary>
    public const int MaxDesktopDelayMs = 30000;

    /// <summary>The parameter that exposes a step's outputs to later steps and segments.</summary>
    public const string StoreAs = "storeAs";

    private static ActionDefinition Desktop(string name, string[] required, string[] optional, string[] outputs) =>
        new(name, "desktop", required, optional, outputs);

    /// <summary>
    /// A browser action. An action that works on an element lists the element targets first,
    /// since choosing the element is the first thing an author does; one that does not
    /// (delay) is not offered selectors it cannot use.
    /// </summary>
    /// <summary>
    /// A browser action. <c>targeted</c> means it can be aimed at an element, so the selectors
    /// are offered; <c>needsElement</c> means it cannot work without one. They differ for an
    /// action that takes an element only to wait for or aim at: navigate needs a url and press
    /// needs a key, and the element is theirs to use or leave.
    /// </summary>
    private static ActionDefinition Browser(string name, string[] required, string[] optional, string[] outputs,
        bool targeted = true, bool needsElement = true) =>
        new(name, "browser", required,
            targeted ? [.. BrowserTargets.Concat(optional).Distinct()] : [.. optional],
            outputs, NeedsElement: targeted && needsElement);

    private static readonly ActionDefinition[] All =
    [
        // desktop
        Desktop("key",           ["key"],         ["window"], ["sendKeysValue"]),
        Desktop("type",          ["value"],       ["window"], ["charactersTyped"]),
        Desktop("delay",         ["ms"],          [], []),
        Desktop("click",         ["coordinates"], ["window", "offset", "rightClick"], ["x", "y"]),
        Desktop("scroll",        ["coordinates"], ["delta", "window"], []),
        Desktop("navigate",      ["window"],      [], ["windowHandle"]),
        Desktop("focus",         [],              ["window", "target"], ["windowHandle"]),
        Desktop("app.launch",    ["executable"],  ["args", "timeoutMs", "attachIfRunning"], ["processId", "windowTitle"]),
        Desktop("notepad.type",  ["text"],        ["windowTitle"], ["charactersTyped"]),
        Desktop("file.saveAs",   ["filePath"],    ["windowTitle", "overwrite", "saveAsShortcut"], ["savedPath", "fileSizeBytes"]),
        Desktop("file.read",     ["path"],        ["maxChars"], ["text", "firstLine", "lineCount"]),
        Desktop("clipboard.get", [],              [], ["text", "firstLine"]),
        Desktop("http.get",      ["url"],         ["timeoutMs"], ["statusCode", "body", "json"]),
        Desktop("http.post",     ["url"],         ["body", "contentType", "timeoutMs"], ["statusCode", "body", "json"]),

        // browser
        Browser("navigate", ["url"],   ["timeoutMs"], [], needsElement: false),
        Browser("fill",     ["value"], ["timeoutMs"], []),
        Browser("type",     ["value"], ["timeoutMs"], []),
        Browser("press",    ["key"],   ["timeoutMs"], [], needsElement: false),
        Browser("click",    [],        ["timeoutMs"], []),
        Browser("dblclick", [],        ["timeoutMs"], []),
        Browser("hover",    [],        ["timeoutMs"], []),
        Browser("scroll",   [],        ["timeoutMs"], []),
        Browser("check",    [],        ["timeoutMs"], []),
        Browser("uncheck",  [],        ["timeoutMs"], []),
        Browser("select",   ["value"], ["timeoutMs"], []),
        Browser("wait",     [],        ["timeoutMs"], []),
        Browser("find",     [],        ["timeoutMs"], []),
        Browser("getText",  [],        ["timeoutMs"], ["value"]),
        Browser("getValue", [],        ["timeoutMs"], ["value"]),
        Browser("getAttr",  ["attribute"], ["timeoutMs"], ["value"]),
        Browser("assert",   ["kind", "expected"], ["timeoutMs"], []),
        Browser("delay",    ["ms"],    [], [], targeted: false),
    ];

    public static IReadOnlyList<ActionDefinition> For(string? runtime)
    {
        var r = Normalise(runtime);
        return [.. All.Where(a => a.Runtime == r).OrderBy(a => a.Name, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> NamesFor(string? runtime) =>
        [.. For(runtime).Select(a => a.Name)];

    public static ActionDefinition? Find(string? runtime, string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var r = Normalise(runtime);
        var name = action.Trim();
        return All.FirstOrDefault(a => a.Runtime == r && a.Name == name)
            // Full runtime names such as browser.element.click are accepted by the host too.
            ?? All.FirstOrDefault(a => a.Runtime == r && name.EndsWith("." + a.Name, StringComparison.Ordinal));
    }

    /// <summary>The outputs an action exposes, or none when the action is not in the catalogue.</summary>
    public static IReadOnlyList<string> OutputsOf(string? runtime, string? action) =>
        Find(runtime, action)?.Outputs ?? [];

    private static string Normalise(string? runtime) =>
        string.IsNullOrWhiteSpace(runtime) ? "desktop" : runtime.Trim().ToLowerInvariant();
}
