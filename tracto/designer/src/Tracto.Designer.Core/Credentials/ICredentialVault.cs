namespace Tracto.Designer.Core.Credentials;

public interface ICredentialVault
{
    void StoreCredential(string name, string value);
    string? ResolveCredential(string name);
    void RemoveCredential(string name);
    IReadOnlyList<CredentialEntry> ListCredentials();
}
