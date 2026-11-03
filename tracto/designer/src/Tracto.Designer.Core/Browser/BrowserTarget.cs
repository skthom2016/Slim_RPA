namespace Tracto.Designer.Core.Browser;

/// <summary>
/// How a recorded page element can be found again at replay time. Each field is one
/// candidate; <see cref="ToParameters"/> picks the most stable one in the order the
/// Browser Runtime resolves them.
/// </summary>
public sealed record BrowserTarget(
    string? Role = null,
    string? Name = null,
    string? Label = null,
    string? Placeholder = null,
    string? TestId = null,
    string? Text = null,
    string? Css = null,
    string? Tag = null,
    string? InputType = null)
{
    public bool IsTextField =>
        Tag is "textarea" ||
        (Tag is "input" && InputType is null or "" or "text" or "search" or "email" or "password" or "tel" or "url" or "number");

    public bool IsCheckable => Tag is "input" && InputType is "checkbox" or "radio";

    public bool IsSelect => Tag is "select";

    /// <summary>Short human description for step text.</summary>
    public string Describe()
    {
        var what = Label ?? Name ?? Placeholder ?? Text ?? TestId ?? Css ?? Tag ?? "element";
        return Role is null ? what : $"{Role} \"{what}\"";
    }

    /// <summary>Script parameters that identify this element, most stable candidate first.</summary>
    public Dictionary<string, string?> ToParameters()
    {
        var p = new Dictionary<string, string?>();

        if (IsTextField || IsSelect || IsCheckable)
        {
            if (!string.IsNullOrWhiteSpace(Label))       { p["label"] = Label; return p; }
            if (!string.IsNullOrWhiteSpace(Placeholder)) { p["placeholder"] = Placeholder; return p; }
        }

        if (!string.IsNullOrWhiteSpace(Role) && !string.IsNullOrWhiteSpace(Name))
        {
            p["role"] = Role;
            p["name"] = Name;
            return p;
        }
        if (!string.IsNullOrWhiteSpace(Label))       { p["label"] = Label; return p; }
        if (!string.IsNullOrWhiteSpace(Placeholder)) { p["placeholder"] = Placeholder; return p; }
        if (!string.IsNullOrWhiteSpace(TestId))      { p["testId"] = TestId; return p; }
        if (!string.IsNullOrWhiteSpace(Text))        { p["text"] = Text; return p; }
        if (!string.IsNullOrWhiteSpace(Css))         { p["css"] = Css; return p; }
        return p;
    }

    /// <summary>Flattens every candidate into a string map for transport inside a RawEvent.</summary>
    public Dictionary<string, string?> ToAttributes() => new()
    {
        ["role"] = Role, ["name"] = Name, ["label"] = Label, ["placeholder"] = Placeholder,
        ["testId"] = TestId, ["text"] = Text, ["css"] = Css, ["tag"] = Tag, ["type"] = InputType
    };

    public static BrowserTarget? FromAttributes(IReadOnlyDictionary<string, string?>? a)
    {
        if (a is null) return null;
        string? G(string k) => a.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        return new BrowserTarget(G("role"), G("name"), G("label"), G("placeholder"), G("testId"), G("text"), G("css"), G("tag"), G("type"));
    }
}
