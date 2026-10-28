namespace Tracto.Designer.Infrastructure.Scripting;

using System.IO;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Infrastructure.Serialization;

/// <summary>
/// File-system-backed script repository using JSON serialization.
/// Path-based public overloads are the primary API; the IScriptRepository
/// explicit implementations treat the scriptId argument as a file path.
/// </summary>
public sealed class FileScriptRepository : IScriptRepository
{
    private readonly JsonScriptSerializer _ser = new();

    // ── Path-based public API (used by ExportImportViewModel and tests) ──────

    public async Task<Script> LoadAsync(string path, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return _ser.Deserialize(content);
    }

    public async Task SaveAsync(Script script, string path, CancellationToken ct = default)
    {
        var content = _ser.Serialize(script);
        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
    }

    // ── IScriptRepository explicit implementations ───────────────────────────

    async Task<Script?> IScriptRepository.LoadAsync(string scriptId, CancellationToken ct)
    {
        if (!File.Exists(scriptId)) return null;
        return await LoadAsync(scriptId, ct).ConfigureAwait(false);
    }

    Task IScriptRepository.SaveAsync(Script script, CancellationToken ct) =>
        SaveAsync(script, script.Id + ".json", ct);
}
