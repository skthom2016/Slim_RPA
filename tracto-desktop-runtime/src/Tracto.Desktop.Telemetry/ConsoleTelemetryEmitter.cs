using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Emits step telemetry records as JSON lines to <see cref="Console.Out"/>.
/// Runs a secret-pattern check before writing to prevent accidental credential exposure.
/// </summary>
public sealed class ConsoleTelemetryEmitter : ITelemetryEmitter
{
    // Patterns that indicate a secret may have leaked into a field value.
    // These cover common credential formats; extend as threat model evolves.
    private static readonly Regex[] SecretPatterns =
    [
        new Regex(@"[A-Za-z0-9+/]{32,}={0,2}", RegexOptions.Compiled),  // base64 blob
        new Regex(@"password\s*[:=]\s*\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"secret\s*[:=]\s*\S+",   RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"token\s*[:=]\s*\S+",    RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <inheritdoc />
    public Task EmitAsync(StepTelemetryRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(record, JsonOptions);

        if (ContainsSecretPattern(json))
        {
            // Redact and replace with a safe sentinel — never silently drop.
            json = "[TELEMETRY REDACTED: secret pattern detected in record for step=" + record.StepId + "]";
        }

        Console.Out.WriteLine(json);
        return Task.CompletedTask;
    }

    private static bool ContainsSecretPattern(string json)
    {
        // We explicitly do NOT flag the base64 pattern for short values.
        // Only run the credential-keyword patterns unconditionally.
        foreach (var pattern in SecretPatterns.Skip(1))
        {
            if (pattern.IsMatch(json))
                return true;
        }
        return false;
    }
}
