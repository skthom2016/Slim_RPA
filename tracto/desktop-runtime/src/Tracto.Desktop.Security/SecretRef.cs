namespace Tracto.Desktop.Security;

/// <summary>
/// A typed reference to a named secret in the credential store.
/// Canonical JSON form: {"ref":"secrets.keyName"}
/// </summary>
/// <param name="Key">The key name used to look up the secret in the credential store.</param>
public sealed record SecretRef(string Key);
