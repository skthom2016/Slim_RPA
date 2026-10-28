namespace Tracto.Designer.Core.Credentials;

using System.Text.RegularExpressions;
using Tracto.Designer.Core.Scripting;

public sealed class CredentialParameterResolver(ICredentialVault vault)
{
    private static readonly Regex Pattern =
        new(@"\{\{credential:([^}]+)\}\}", RegexOptions.Compiled);

    public Dictionary<string, string?> ResolveParameters(ScriptStep step)
    {
        var resolved = new Dictionary<string, string?>(step.Parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var key in resolved.Keys.ToList())
        {
            if (resolved[key] is string v && Pattern.IsMatch(v))
                resolved[key] = Pattern.Replace(v, m => vault.ResolveCredential(m.Groups[1].Value) ?? m.Value);
        }
        return resolved;
    }

    public bool HasCredentialPlaceholders(ScriptStep step) =>
        step.Parameters.Values.Any(v => v is not null && Pattern.IsMatch(v));
}
