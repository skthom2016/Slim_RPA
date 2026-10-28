namespace Tracto.Designer.Core.Scripting;

public interface IScriptRepository
{
    Task<Script?> LoadAsync(string scriptId, CancellationToken ct = default);
    Task SaveAsync(Script script, CancellationToken ct = default);
}
