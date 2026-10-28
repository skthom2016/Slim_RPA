namespace Tracto.Designer.Core.Templates;

using Tracto.Designer.Core.Scripting;

public sealed record ScriptTemplate(
    string Id,
    string Name,
    string Description,
    TemplateCategory Category,
    IReadOnlyList<string> Tags,
    Func<Script> CreateScript);
