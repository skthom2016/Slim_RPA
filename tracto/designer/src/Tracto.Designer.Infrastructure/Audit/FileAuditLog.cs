namespace Tracto.Designer.Infrastructure.Audit;

using System.IO;
using System.Text.Json;
using Tracto.Designer.Core.Audit;

public sealed class FileAuditLog : IAuditLog, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileAuditLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Log(AuditEntry entry)
    {
        _semaphore.Wait();
        try
        {
            File.AppendAllText(_path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
            // audit log must never throw
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public IReadOnlyList<AuditEntry> GetEntries(AuditCategory? category = null)
    {
        _semaphore.Wait();
        try
        {
            if (!File.Exists(_path)) return [];
            var entries = File.ReadAllLines(_path)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l =>
                {
                    try { return JsonSerializer.Deserialize<AuditEntry>(l); }
                    catch { return null; }
                })
                .Where(e => e is not null)
                .Cast<AuditEntry>();
            return category is null ? [..entries] : [..entries.Where(e => e.Category == category)];
        }
        catch
        {
            return [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() { _semaphore.Dispose(); }
}
