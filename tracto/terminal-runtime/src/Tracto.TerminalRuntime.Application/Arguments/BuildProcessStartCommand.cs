using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Application.Arguments;

/// <summary>
/// Input command for the <see cref="BuildProcessStartHandler"/>.
/// Carries all user-supplied data that must be validated before a
/// <see cref="ProcessStartSpec"/> can be constructed.
/// </summary>
/// <param name="Shell">Target shell type.</param>
/// <param name="ExecutablePath">Absolute path to the shell executable.</param>
/// <param name="Arguments">Ordered list of typed arguments (name + value pairs).</param>
/// <param name="WorkingDirectory">Working directory path (will be validated against policy).</param>
/// <param name="EnvironmentVars">Environment variable overrides.</param>
/// <param name="UrlPolicy">
///   Policy controlling which URL hosts and filesystem path roots are allowed in argument values.
/// </param>
public sealed record BuildProcessStartCommand(
    ShellType Shell,
    string ExecutablePath,
    IReadOnlyList<CommandArgument> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVars,
    AllowedUrlRootsPolicy UrlPolicy);
