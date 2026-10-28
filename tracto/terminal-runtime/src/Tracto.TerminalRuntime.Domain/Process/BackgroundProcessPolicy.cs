namespace Tracto.TerminalRuntime.Domain.Process;

/// <summary>
/// Domain policy: detects background-process patterns in a process argument list.
///
/// CEC TER-US-015 AC-7: scripts that use background spawning (Bash <c>&amp;</c>,
/// PowerShell <c>Start-Job</c>, etc.) are blocked unless the approved command template
/// explicitly sets <c>AllowsBackground = true</c>.
///
/// The check is intentionally conservative: any argument token containing one of the
/// known background markers is flagged.  No shell-string interpretation is performed —
/// each token from the typed ArgumentList is scanned as-is.
/// </summary>
public static class BackgroundProcessPolicy
{
    // Bash / sh / WSL: trailing & in a shell expression; nohup is another vector.
    // PowerShell: Start-Job, Start-Process -NoNewWindow, &amp; (invoke operator on job-producing commands).
    // These are scanned as substrings within individual argument tokens.
    private static readonly string[] BackgroundMarkers =
    [
        " &",          // Bash background job: cmd args &
        "&\n",         // same pattern at line end
        "& ",          // PowerShell invoke operator followed by space
        "&\"",         // & followed by quoted string (PS invoke)
        "&'",          // & followed by single-quoted string (PS invoke)
        "Start-Job",   // PowerShell background job cmdlet
        "Start-Process",// PowerShell spawns a separate process (commonly background)
        "nohup",       // Unix detach
        "setsid",      // Unix session leader (detaches from terminal)
        "disown",      // Bash: detach a job from the shell
        "bg ",         // Bash: bg builtin
    ];

    /// <summary>
    /// Returns <see langword="true"/> when any argument token in
    /// <paramref name="argumentList"/> contains a background-spawning pattern.
    /// </summary>
    public static bool ContainsBackgroundPattern(IReadOnlyList<string> argumentList)
    {
        foreach (var token in argumentList)
        {
            foreach (var marker in BackgroundMarkers)
            {
                if (token.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first background marker found in <paramref name="argumentList"/>,
    /// or <see langword="null"/> if none is detected.
    /// Used to produce a precise rejection message.
    /// </summary>
    public static string? FindFirstMarker(IReadOnlyList<string> argumentList)
    {
        foreach (var token in argumentList)
        {
            foreach (var marker in BackgroundMarkers)
            {
                if (token.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return marker.Trim();
            }
        }

        return null;
    }
}
