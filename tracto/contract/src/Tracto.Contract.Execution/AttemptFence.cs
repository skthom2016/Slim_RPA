#nullable enable

using System.Collections.Generic;
using System.Linq;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public sealed class AttemptFence
{
    // Returns true if the previous attempt's run state blocks a new attempt dispatch.
    public bool IsNewAttemptBlocked(RunState previousAttemptState)
    {
        // UNKNOWN terminates the attempt and blocks re-dispatch without affirmative reconciliation.
        return previousAttemptState == RunState.Unknown;
    }

    // Returns a republish diagnostic message when a schema version is unsupported.
    public static string GetRepublishDiagnostic(string unsupportedVersion, IReadOnlyList<int> supportedMajors)
        => $"Artifact schema version '{unsupportedVersion}' is not supported. Supported majors: [{string.Join(", ", supportedMajors)}]. Republish the artifact with a supported schema version.";
}
