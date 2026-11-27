using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace TractoRuntime.Browser.Host;

/// <summary>One stored credential as the Orchestrator hands it to a run.</summary>
public sealed record CredentialValues(string Username, string Password, string? Totp);

/// <summary>The Orchestrator refused to give a run a credential or setting.</summary>
public sealed class CredentialRefusedException(string message) : Exception(message);

/// <summary>
/// The credentials and settings one run reads: <c>${credential:NAME.username|password|totp}</c> and
/// <c>${setting:NAME}</c>. They are fetched from the Orchestrator before the browser opens, held in
/// memory for this run only, and never written to disk.
/// </summary>
public sealed class RunSecrets
{
    // A name may hold ${...} parts (a job parameter), so the pattern lets a name contain them.
    private static readonly Regex CredRef = new(
        @"\$\{credential:((?:[^${}]|\$\{[^}]*\})+)\.(username|password|totp)\}", RegexOptions.Compiled);
    private static readonly Regex SettingRef = new(
        @"\$\{setting:((?:[^${}]|\$\{[^}]*\})+)\}", RegexOptions.Compiled);
    private static readonly Regex Part = new(@"\$\{([^}]*)\}", RegexOptions.Compiled);

    // Stands in for a value while the rest of a parameter is expanded, so a password that happens to
    // contain ${...} is never expanded itself.
    private const char Open = '', Close = '';

    public IReadOnlyDictionary<string, CredentialValues> Credentials { get; private set; } =
        new Dictionary<string, CredentialValues>();
    public IReadOnlyDictionary<string, string> Settings { get; private set; } = new Dictionary<string, string>();

    public bool IsEmpty => Credentials.Count == 0 && Settings.Count == 0;

    public void Load(IReadOnlyDictionary<string, CredentialValues> credentials, IReadOnlyDictionary<string, string> settings)
    {
        Credentials = credentials;
        Settings = settings;
    }

    /// <summary>Every value that must not reach a log, an output or a result.</summary>
    public IEnumerable<string> MaskValues()
    {
        foreach (var c in Credentials.Values)
        {
            yield return c.Username;
            yield return c.Password;
            if (!string.IsNullOrEmpty(c.Totp)) yield return c.Totp;
        }
    }

    /// <summary>The passwords and codes only: what a stored output must never hold.</summary>
    private IEnumerable<string> SecretOnly()
    {
        foreach (var c in Credentials.Values)
        {
            yield return c.Password;
            if (!string.IsNullOrEmpty(c.Totp)) yield return c.Totp;
        }
    }

    // ── names ────────────────────────────────────────────────────────────────

    /// <summary>A reference's name with its ${param.x} parts filled in from vars.</summary>
    private static string ExpandName(string name, IReadOnlyDictionary<string, string> vars) =>
        Part.Replace(name, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>
    /// The credential and setting names the script reads, after job parameters are filled in.
    /// A name that still holds a ${...} part (an output of an earlier step) is refused: the
    /// Orchestrator can only check names that are known when the run starts.
    /// </summary>
    public static (SortedSet<string> Credentials, SortedSet<string> Settings) CollectNames(
        IEnumerable<(string StepId, IReadOnlyDictionary<string, string?> Parameters)> steps,
        IReadOnlyDictionary<string, string> vars)
    {
        var creds = new SortedSet<string>(StringComparer.Ordinal);
        var settings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (stepId, parameters) in steps)
        {
            foreach (var value in parameters.Values)
            {
                if (value is null || !value.Contains("${", StringComparison.Ordinal)) continue;
                foreach (Match m in CredRef.Matches(value))
                    creds.Add(Checked(stepId, ExpandName(m.Groups[1].Value, vars)));
                foreach (Match m in SettingRef.Matches(value))
                    settings.Add(Checked(stepId, ExpandName(m.Groups[1].Value, vars)));
            }
        }
        return (creds, settings);
    }

    private static string Checked(string stepId, string name)
    {
        if (name.Contains("${", StringComparison.Ordinal))
            throw new ScriptValidationException(
                $"{stepId}: a credential or setting name can only be written out or built from job parameters, got '{name}'.");
        return name;
    }

    // ── substituting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Step 1 of 2: replaces each credential and setting reference with a stand-in, so that the
    /// ordinary variable expansion that follows cannot touch the real values.
    /// </summary>
    public Dictionary<string, string?> Protect(
        IReadOnlyDictionary<string, string?> parameters, IReadOnlyDictionary<string, string> vars, List<string> values)
    {
        var result = new Dictionary<string, string?>(parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var key in result.Keys.ToList())
        {
            var v = result[key];
            if (v is null || !v.Contains("${", StringComparison.Ordinal)) continue;

            v = CredRef.Replace(v, m =>
            {
                var name = ExpandName(m.Groups[1].Value, vars);
                if (!Credentials.TryGetValue(name, out var c))
                    throw new ScriptValidationException($"credential '{name}' was not given to this run");
                var field = m.Groups[2].Value switch
                {
                    "username" => c.Username,
                    "password" => c.Password,
                    _ => c.Totp ?? throw new ScriptValidationException(
                        $"credential '{name}' has no one-time-code key, so ${{credential:{name}.totp}} cannot be used")
                };
                values.Add(field);
                return $"{Open}{values.Count - 1}{Close}";
            });
            v = SettingRef.Replace(v, m =>
            {
                var name = ExpandName(m.Groups[1].Value, vars);
                if (!Settings.TryGetValue(name, out var s))
                    throw new ScriptValidationException($"setting '{name}' was not given to this run");
                values.Add(s);
                return $"{Open}{values.Count - 1}{Close}";
            });
            result[key] = v;
        }
        return result;
    }

    /// <summary>Step 2 of 2: puts the real values where the stand-ins are.</summary>
    public static void Reveal(Dictionary<string, string?> parameters, List<string> values)
    {
        if (values.Count == 0) return;
        foreach (var key in parameters.Keys.ToList())
        {
            var v = parameters[key];
            if (v is null || v.IndexOf(Open) < 0) continue;
            parameters[key] = Regex.Replace(v, $"{Open}([0-9]+){Close}", m => values[int.Parse(m.Groups[1].Value)]);
        }
    }

    // ── outputs ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True when text holds a credential's password or one-time code: such a value must not become
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
/// The values of the current run's credentials, replaced by <c>●●●●</c> wherever the worker writes
/// text that leaves the run: its console, the run log, the outputs and the result. A run is the
/// only thing the worker does at a time, so one set of values is enough.
/// </summary>
public static class SecretMask
{
    public const string Dots = "●●●●";

    /// <summary>
    /// Values shorter than this are not replaced: masking a two letter value would blank out
    /// ordinary words. Credentials are expected to be longer.
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

    /// <summary>The outcome with its message, step errors and outputs masked.</summary>
    public static RunOutcome Mask(RunOutcome o) => o with
    {
        Message = o.Message is null ? null : Apply(o.Message),
        Steps = o.Steps.Select(s => s with { Error = s.Error is null ? null : Apply(s.Error) }).ToList(),
        Outputs = o.Outputs.ToDictionary(kv => kv.Key, kv => Apply(kv.Value), StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>
/// The console format the worker already used (one line per entry), with the run's credential
/// values masked.
/// </summary>
public sealed class MaskingConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "tracto-masked";

    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (logEntry.Exception is not null) message += " " + logEntry.Exception;
        message = SecretMask.Apply(message).ReplaceLineEndings(" ");

        var level = logEntry.LogLevel switch
        {
            LogLevel.Trace => "trce", LogLevel.Debug => "dbug", LogLevel.Information => "info",
            LogLevel.Warning => "warn", LogLevel.Error => "fail", LogLevel.Critical => "crit", _ => "none",
        };
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("HH:mm:ss ")).Append(level).Append(": ")
          .Append(logEntry.Category).Append('[').Append(logEntry.EventId.Id).Append("] ").Append(message);
        textWriter.WriteLine(sb.ToString());
    }
}
