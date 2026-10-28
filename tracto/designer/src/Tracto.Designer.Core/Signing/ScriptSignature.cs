namespace Tracto.Designer.Core.Signing;

public sealed record ScriptSignature(
    string SignerId,
    string Algorithm,
    DateTimeOffset Timestamp,
    string Digest,
    string? PublicKeyThumbprint = null);
