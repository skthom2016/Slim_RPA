namespace Tracto.Designer.Infrastructure.Publication;

using System.IO;
using System.Text.Json;
using Tracto.Designer.Core.Publication;

public sealed class LocalFilePublisher : IScriptPublisher
{
    private readonly string _outputDirectory;
    private readonly PublicationGate _gate;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    // SemaphoreSlim(1,1) serialises concurrent publish calls so that the catalog
    // read → duplicate-check → write sequence is atomic (DS-16-AC06, TOCTOU fix).
    private readonly SemaphoreSlim _catalogLock = new(1, 1);

    public LocalFilePublisher(string outputDirectory, PublicationGate gate)
    {
        _outputDirectory = outputDirectory;
        _gate = gate;
        Directory.CreateDirectory(outputDirectory);
    }

    public string Name => "Local File Publisher";

    public async Task<PublicationResult> PublishAsync(PublicationRequest req, CancellationToken ct = default)
    {
        var gateResult = _gate.CheckGates(req);
        if (gateResult is not null) return gateResult;

        var s = req.Script;
        var catalogPath = Path.Combine(_outputDirectory, "catalog.json");

        // Hold the lock for the entire read → duplicate-check → write sequence so that
        // two concurrent PublishAsync calls cannot both pass the duplicate check and each
        // write an entry, which would silently corrupt the catalog (DS-16-AC06).
        await _catalogLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var catalog = await LoadCatalogAsync(catalogPath).ConfigureAwait(false);

            // Check no duplicate id+version
            if (catalog.Any(e => e.Id == s.Id && e.Version == s.Version))
                return PublicationResult.Failure($"Script '{s.Id}' version '{s.Version}' is already published. Bump version before re-publishing.");

            var pubId = Guid.NewGuid().ToString("N");
            var fileName = $"{s.Id}_{s.Version}.script.json";
            var filePath = Path.Combine(_outputDirectory, fileName);

            var envelope = new
            {
                publicationId = pubId,
                publishedAt = DateTimeOffset.UtcNow,
                signature = new { req.Signature.SignerId, req.Signature.Algorithm, req.Signature.Digest },
                script = new
                {
                    s.Id,
                    s.Name,
                    s.Version,
                    s.TargetRuntime,
                    steps = s.Steps.Select(st => new { st.StepNumber, st.Action, st.Description, st.Parameters, st.Condition })
                }
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(envelope, _json), ct).ConfigureAwait(false);

            catalog.Add(new CatalogEntry(s.Id, s.Name, s.Version, s.TargetRuntime, DateTimeOffset.UtcNow, fileName, req.Signature.Digest));
            await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(catalog, _json), ct).ConfigureAwait(false);

            return PublicationResult.Ok(pubId);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    private static async Task<List<CatalogEntry>> LoadCatalogAsync(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<CatalogEntry>>(json) ?? [];
        }
        catch { return []; }
    }

    private sealed record CatalogEntry(
        string Id,
        string Name,
        string Version,
        string TargetRuntime,
        DateTimeOffset PublishedAt,
        string FilePath,
        string SignatureDigest);
}
