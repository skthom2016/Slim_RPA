namespace Tracto.Designer.Core.Signing;

using System.Security.Cryptography;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

public sealed class HmacScriptSigner : IScriptSigner
{
    private const string Algo = "HMAC-SHA256";
    private readonly byte[] _key;
    private readonly IScriptValidator _validator;
    private readonly string _signerId;

    public HmacScriptSigner(string signerId, byte[] key, IScriptValidator validator)
    {
        _signerId = signerId;
        _key = key;
        _validator = validator;
    }

    public string SignerId => _signerId;

    public ScriptSignature Sign(Script script)
    {
        var result = _validator.Validate(script);
        if (!result.IsValid)
        {
            var firstError = result.Errors.FirstOrDefault(e => e.Severity == ValidationSeverity.Error);
            var message = firstError?.Message ?? result.Errors.FirstOrDefault()?.Message ?? "Validation failed with unspecified errors.";
            throw new InvalidOperationException($"Script is not valid; cannot sign. First error: {message}");
        }

        var bytes = ScriptCanonicalizer.CanonicalBytes(script);
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(bytes);
        return new ScriptSignature(_signerId, Algo, DateTimeOffset.UtcNow, Convert.ToHexString(hash));
    }

    public bool Verify(Script script, ScriptSignature sig)
    {
        if (sig.Algorithm != Algo || sig.SignerId != _signerId) return false;
        try
        {
            var bytes = ScriptCanonicalizer.CanonicalBytes(script);
            using var hmac = new HMACSHA256(_key);
            var expected = Convert.ToHexString(hmac.ComputeHash(bytes));
            return string.Equals(expected, sig.Digest, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
