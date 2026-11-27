#nullable enable
using System.Text.RegularExpressions;

namespace Tracto.Desktop.Protocol;

/// <summary>One environment variable as the Orchestrator hands it to a run.</summary>
public sealed record EnvValue(string Value, bool Secret);

/// <summary>The Orchestrator refused to give a run a variable.</summary>
public sealed class CredentialRefusedException(string message) : Exception(message);

/// <summary>A script refers to a variable in a way that cannot work.</summary>
public sealed class CredentialScriptException(string message) : Exception(message);

/// <summary>
/// The environment variables one run reads: <c>${env:NAME}</c>. They are fetched from the
/// Orchestrator before the first step, held in memory for this run only, and never written to
/// disk. A value flagged secret is masked and may not become an output.
/// </summary>
public sealed class RunSecrets
{
    // A name may hold ${...} parts (a job parameter), so the pattern lets a name contain them.
    private static readonly Regex EnvRef = new(
        @"\$\{env:((?:[^${}]|\$\{[^}]*\})+)\}", RegexOptions.Compiled);
    private static readonly Regex Part = new(@"\$\{([^}]*)\}", RegexOptions.Compiled);

    // Stands in for a value while the rest of a parameter is expanded, so a value that happens to
    // contain a variable is never expanded itself.
    private const char Open = '\uE000', Close = '\uE001';

    public IReadOnlyDictionary<string, EnvValue> Values { get; private set; } = new Dictionary<string, EnvValue>();

    public bool IsEmpty => Values.Count == 0;

    public void Load(IReadOnlyDictionary<string, EnvValue> values) => Values = values;

    /// <summary>Every value that must not reach a log, an output or a result (the secret ones).</summary>
    public IEnumerable<string> MaskValues() => SecretOnly();

    private IEnumerable<string> SecretOnly()
    {
        foreach (var v in Values.Values)
            if (v.Secret && !string.IsNullOrEmpty(v.Value)) yield return v.Value;
    }

    // ── names ────────────────────────────────────────────────────────────────

    private static string ExpandName(string name, IReadOnlyDictionary<string, string> vars) =>
        Part.Replace(name, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>
    /// The variable names the script reads, after job parameters are filled in. A name that still
    /// holds a variable (an output of an earlier step) is refused: the Orchestrator can only
    /// check names that are known when the run starts.
    /// </summary>
    public static SortedSet<string> CollectNames(
        IEnumerable<(string StepId, IReadOnlyDictionary<string, string?> Parameters)> steps,
        IReadOnlyDictionary<string, string> vars)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (stepId, parameters) in steps)
        {
            foreach (var value in parameters.Values)
            {
                if (value is null || !value.Contains("${", StringComparison.Ordinal)) continue;
                foreach (Match m in EnvRef.Matches(value))
                    names.Add(Checked(stepId, ExpandName(m.Groups[1].Value, vars)));
            }
        }
        return names;
    }

    private static string Checked(string stepId, string name)
    {
        if (name.Contains("${", StringComparison.Ordinal))
            throw new CredentialScriptException(
                $"{stepId}: a variable name can only be written out or built from job parameters, got '{name}'.");
        return name;
    }

    // ── substituting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Step 1 of 2: replaces each variable reference in the text values with a
    /// stand-in, so that the ordinary variable expansion that follows cannot touch the real values.
    /// </summary>
    public Dictionary<string, object?> Protect(
        IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, string> vars, List<string> values)
    {
        var result = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var key in result.Keys.ToList())
        {
            if (result[key] is not string v || !v.Contains("${", StringComparison.Ordinal)) continue;

            v = EnvRef.Replace(v, m =>
            {
                var name = ExpandName(m.Groups[1].Value, vars);
                if (!Values.TryGetValue(name, out var e))
                    throw new CredentialScriptException($"variable '{name}' was not given to this run");
                values.Add(e.Value);
                return $"{Open}{values.Count - 1}{Close}";
            });
            result[key] = v;
        }
        return result;
    }

    /// <summary>Step 2 of 2: puts the real values where the stand-ins are.</summary>
    public static void Reveal(Dictionary<string, object?> parameters, List<string> values)
    {
        if (values.Count == 0) return;
        foreach (var key in parameters.Keys.ToList())
        {
            if (parameters[key] is not string v || v.IndexOf(Open) < 0) continue;
            parameters[key] = Regex.Replace(v, $"{Open}([0-9]+){Close}", m => values[int.Parse(m.Groups[1].Value)]);
        }
    }

    // ── outputs ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True when text holds a secret variable's value: such a value must not become
    /// a stored output of the run.
    /// </summary>
    public bool HoldsSecret(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var s in SecretOnly())
            if (s.Length >= SecretMask.MinLength && text.Contains(s, StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>
/// The secret values of the current run, replaced by dots wherever the worker writes text
/// that leaves the run: its console, the run log, the outputs and the result. A run is the only
/// thing the worker does at a time, so one set of values is enough.
/// </summary>
public static class SecretMask
{
    public const string Dots = "●●●●";

    /// <summary>
    /// Values shorter than this are not replaced: masking a two letter value would blank out
    /// ordinary words. Secrets are expected to be longer.
    /// </summary>
    public const int MinLength = 4;

    private static volatile string[] _values = [];

    public static void Set(IEnumerable<string> values) =>
        _values = values.Where(v => v.Length >= MinLength).Distinct(StringComparer.Ordinal)
                        .OrderByDescending(v => v.Length).ToArray();

    public static void Clear() => _values = [];

    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        foreach (var v in _values)
            if (text.Contains(v, StringComparison.Ordinal)) text = text.Replace(v, Dots, StringComparison.Ordinal);
        return text;
    }
}
