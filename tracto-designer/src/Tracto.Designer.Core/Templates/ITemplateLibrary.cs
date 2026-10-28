namespace Tracto.Designer.Core.Templates;

public interface ITemplateLibrary
{
    IReadOnlyList<ScriptTemplate> GetAll();
    IReadOnlyList<ScriptTemplate> GetByCategory(TemplateCategory category);
    IReadOnlyList<ScriptTemplate> Search(string query);
}
