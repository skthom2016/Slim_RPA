using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Domain.Arguments;

/// <summary>
/// An immutable, fully-validated specification for launching a shell process.
/// All argument values in <see cref="ArgumentList"/> have been schema-validated and
/// are ready to be passed verbatim through the typed <c>ProcessStartInfo.ArgumentList</c> API.
/// No shell-string concatenation of user-supplied values is permitted.
/// </summary>
/// <param name="Shell">The shell type to invoke.</param>
/// <param name="ExecutablePath">Absolute path to the shell executable.</param>
/// <param name="ArgumentList">
///   Ordered list of argument values to pass via <c>ProcessStartInfo.ArgumentList</c>.
///   Each value is treated as a literal token — the OS does NOT re-interpret it.
/// </param>
/// <param name="WorkingDirectory">Canonicalized, policy-approved working directory.</param>
/// <param name="EnvironmentVars">
///   Key-value environment variable overrides.  Values must not carry plaintext secrets.
/// </param>
public sealed record ProcessStartSpec(
    ShellType Shell,
    string ExecutablePath,
    IReadOnlyList<string> ArgumentList,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVars);
