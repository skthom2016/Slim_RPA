using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Infrastructure.Arguments;

/// <summary>
/// Builds a <see cref="ProcessStartInfo"/> from a validated <see cref="ProcessStartSpec"/>.
///
/// SECURITY CONTRACT (CRITICAL fix — shell metacharacter injection):
/// <para>
/// The typed <c>ProcessStartInfo.ArgumentList</c> API prevents injection only for programs
/// that read <c>argv[]</c> directly.  Shell interpreters (cmd.exe, pwsh, bash) are different:
/// the .NET runtime joins the ArgumentList tokens into a quoted command-line string, and the
/// shell then strips the enclosing quotes and interprets the result as shell syntax.  A value
/// such as <c>data&amp; echo pwned</c> passed after <c>/C</c> (cmd) or <c>-Command</c>
/// (PowerShell) therefore triggers a secondary command at runtime.
/// </para>
/// <para>
/// FIX — user-supplied argument values are NEVER appended to <c>psi.ArgumentList</c>.
/// Instead they are injected as environment variables (<c>TRACTO_ARG_0</c>,
/// <c>TRACTO_ARG_1</c>, …, <c>TRACTO_ARG_COUNT</c>).  Each shell type then handles the
/// data as follows:
/// <list type="bullet">
///   <item>
///     <b>PowerShell</b> — invoked with a static, pre-encoded <c>-EncodedCommand</c> payload
///     (Base64 UTF-16LE).  The encoded script reads the TRACTO_ARG_* env vars and invokes
///     the command via the PowerShell call operator (<c>&amp;</c>), which treats the first
///     value as a command <em>name</em>, not as shell syntax.  Metacharacters in the name or
///     parameters are therefore inert.
///   </item>
///   <item>
///     <b>cmd.exe / WslBash</b> — these shells cannot safely execute user-supplied commands
///     without string evaluation.  <c>Build</c> throws <see cref="InvalidOperationException"/>
///     when <see cref="ProcessStartSpec.ArgumentList"/> is non-empty.  All dynamic data must
///     be passed exclusively through <see cref="ProcessStartSpec.EnvironmentVars"/>, and the
///     actual command must be a static, pre-approved script whose path is configured outside
///     this builder.
///   </item>
/// </list>
/// </para>
/// <para>
/// <c>ProcessStartInfo.Arguments</c> (the legacy string form) is NEVER set.
/// </para>
/// </summary>
public sealed class ProcessStartSpecBuilder
{
    // ── Shell executable mappings ─────────────────────────────────────────────

    private static readonly Dictionary<ShellType, string> ShellExecutables =
        new(3)
        {
            [ShellType.Cmd]        = @"cmd.exe",
            [ShellType.PowerShell] = @"pwsh.exe",
            [ShellType.WslBash]    = @"wsl.exe",
        };

    // ── PowerShell static encoded command ────────────────────────────────────
    //
    // This Base64(UTF-16LE) string is computed ONCE at class initialization from
    // a STATIC PowerShell script.  No user data is ever interpolated into the
    // script string or the Base64 value.
    //
    // The script:
    //   $c=[int]$env:TRACTO_ARG_COUNT;
    //   if($c -gt 0){
    //     $cmd=[Environment]::GetEnvironmentVariable('TRACTO_ARG_0');
    //     $params=if($c -gt 1){
    //       1..($c-1)|ForEach-Object{[Environment]::GetEnvironmentVariable("TRACTO_ARG_$_")}
    //     }else{@()};
    //     & $cmd @params
    //   }
    //
    // Security note: the PowerShell call operator (&) treats $cmd as a command NAME,
    // not as a shell script string.  A value such as "data& echo pwned" would be looked
    // up as a command whose literal name is "data& echo pwned" — the & is not a shell
    // operator in this context.  $params elements are bound as argv tokens to the called
    // command, so metacharacters in them are also inert.

    private const string PsArgCountEnvVar = "TRACTO_ARG_COUNT";
    private const string PsArgEnvVarPrefix = "TRACTO_ARG_";

    private static readonly string PsEncodedCommand = Convert.ToBase64String(
        Encoding.Unicode.GetBytes(
            "$c=[int]$env:TRACTO_ARG_COUNT;" +
            "if($c -gt 0){" +
            "$cmd=[Environment]::GetEnvironmentVariable('TRACTO_ARG_0');" +
            "$params=if($c -gt 1){" +
            "1..($c-1)|ForEach-Object{[Environment]::GetEnvironmentVariable(\"TRACTO_ARG_$_\")}" +
            "}else{@()};" +
            "& $cmd @params" +
            "}"));

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ProcessStartInfo"/> from a fully-validated
    /// <see cref="ProcessStartSpec"/>.
    /// </summary>
    /// <param name="spec">A fully-validated process start specification.</param>
    /// <returns>A configured <see cref="ProcessStartInfo"/> ready for <c>Process.Start</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="spec"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="spec"/> targets cmd.exe or WslBash AND
    /// <see cref="ProcessStartSpec.ArgumentList"/> is non-empty.  These shells cannot safely
    /// receive user-supplied data via the command line.
    /// </exception>
    public ProcessStartInfo Build(ProcessStartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var exePath = string.IsNullOrWhiteSpace(spec.ExecutablePath)
            ? ResolveShellExecutable(spec.Shell)
            : spec.ExecutablePath;

        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            WorkingDirectory       = spec.WorkingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = false,
            CreateNoWindow         = true,
            // CRITICAL: do NOT set psi.Arguments — use ArgumentList for static tokens only.
        };

        // Add static shell prefix tokens.  For cmd.exe / WslBash this also guards that
        // ArgumentList is empty (throws if non-empty — see class-level doc).
        AddShellPrefixArguments(psi, spec.Shell, spec.ArgumentList);

        // CRITICAL FIX: inject user-supplied argument values as environment variables.
        // They are NEVER appended to psi.ArgumentList and therefore never reach the shell
        // interpreter as part of a command-line string that is evaluated as shell syntax.
        InjectArgsAsEnvironmentVariables(psi, spec.ArgumentList);

        // Overlay caller-supplied environment variable overrides.
        foreach (var (key, val) in spec.EnvironmentVars)
        {
            psi.Environment[key] = val;
        }

        return psi;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string ResolveShellExecutable(ShellType shell) =>
        ShellExecutables.TryGetValue(shell, out var exe)
            ? exe
            : throw new NotSupportedException($"No executable mapping for shell type '{shell}'.");

    /// <summary>
    /// Adds the static prefix arguments for the given shell type to
    /// <paramref name="psi"/>.<c>ArgumentList</c>.
    ///
    /// For cmd.exe and WslBash: throws <see cref="InvalidOperationException"/> when
    /// <paramref name="argumentList"/> is non-empty, because these shells cannot safely
    /// receive user-supplied data via the command line.
    ///
    /// For PowerShell: uses a static <c>-EncodedCommand</c> payload; user data is
    /// delivered exclusively via environment variables (TRACTO_ARG_*).
    /// </summary>
    private static void AddShellPrefixArguments(
        ProcessStartInfo psi,
        ShellType shell,
        IReadOnlyList<string> argumentList)
    {
        switch (shell)
        {
            case ShellType.Cmd:
                // cmd.exe interprets the string assembled from ArgumentList tokens as shell
                // syntax after the /C flag.  A value containing & | ; or other metacharacters
                // can trigger secondary commands even when the typed ArgumentList API is used.
                // User data MUST be passed exclusively through EnvironmentVars; the actual
                // command must be a static, pre-approved .cmd script configured outside this builder.
                if (argumentList.Count > 0)
                    throw new InvalidOperationException(
                        "cmd.exe does not support safe argument passing via ArgumentList. " +
                        "Pass all dynamic data through ProcessStartSpec.EnvironmentVars and " +
                        "configure a static, pre-approved .cmd script path as the shell command.");

                psi.ArgumentList.Add("/D");  // disable AutoRun registry keys
                psi.ArgumentList.Add("/Q");  // echo off (quiet)
                break;

            case ShellType.PowerShell:
                // The static encoded command reads TRACTO_ARG_COUNT + TRACTO_ARG_N from env vars
                // and invokes via the call operator (&), which treats the value as a command
                // NAME — metacharacters in argument values are therefore inert.
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-EncodedCommand");
                psi.ArgumentList.Add(PsEncodedCommand);
                break;

            case ShellType.WslBash:
                // bash -c interprets its argument string as a shell script.  Passing user data
                // there (even via ArgumentList) allows metacharacter injection at runtime.
                // User data MUST be passed exclusively through EnvironmentVars; the actual
                // script must be a static, pre-approved bash script configured outside this builder.
                if (argumentList.Count > 0)
                    throw new InvalidOperationException(
                        "WSL bash does not support safe argument passing via ArgumentList. " +
                        "Pass all dynamic data through ProcessStartSpec.EnvironmentVars and " +
                        "configure a static, pre-approved bash script path as the shell command.");

                psi.ArgumentList.Add("--");    // end of wsl.exe options
                psi.ArgumentList.Add("bash");  // invoke bash
                psi.ArgumentList.Add("-e");    // exit-on-error
                break;

            default:
                throw new NotSupportedException($"No prefix arguments defined for shell type '{shell}'.");
        }
    }

    /// <summary>
    /// Injects each value in <paramref name="args"/> as a numbered environment variable
    /// (<c>TRACTO_ARG_0</c>, <c>TRACTO_ARG_1</c>, …) and sets <c>TRACTO_ARG_COUNT</c>.
    ///
    /// Values are opaque data tokens.  They are never appended to
    /// <c>psi.ArgumentList</c> and therefore never reach the shell interpreter as part
    /// of a command-line string that would be evaluated as shell syntax.
    /// </summary>
    private static void InjectArgsAsEnvironmentVariables(
        ProcessStartInfo psi,
        IReadOnlyList<string> args)
    {
        psi.Environment[PsArgCountEnvVar] =
            args.Count.ToString(CultureInfo.InvariantCulture);

        for (int i = 0; i < args.Count; i++)
        {
            psi.Environment[$"{PsArgEnvVarPrefix}{i}"] = args[i];
        }
    }
}
