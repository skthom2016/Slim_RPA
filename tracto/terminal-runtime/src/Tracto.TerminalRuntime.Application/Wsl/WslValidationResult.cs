namespace Tracto.TerminalRuntime.Application.Wsl;

/// <summary>
/// Success payload returned by <see cref="IWslCommandValidator.ValidateAsync"/>.
/// Contains the fully-constructed argument list ready to be handed to <c>wsl.exe</c>.
/// No plaintext secrets are included (CEC invariant #4).
/// </summary>
/// <param name="WslExePath">
///   Absolute path to <c>wsl.exe</c> on the Windows host (typically
///   <c>%SystemRoot%\System32\wsl.exe</c>).
/// </param>
/// <param name="ArgumentList">
///   Ordered argument tokens for <c>wsl.exe</c>:
///   <c>-d {distro} -u {user} -- {scriptPath} [args...]</c>.
///   Passed as an array so callers can use <c>ProcessStartInfo.ArgumentList</c>
///   without shell-quoting concerns.
/// </param>
public sealed record WslValidationResult(
    string WslExePath,
    IReadOnlyList<string> ArgumentList);
