namespace Tracto.Designer.Core.Credentials;

public sealed record CredentialEntry(string Name, string ObfuscatedHint, DateTimeOffset StoredAt);
