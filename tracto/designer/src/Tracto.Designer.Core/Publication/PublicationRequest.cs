namespace Tracto.Designer.Core.Publication;

using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Testing;

/// <param name="Script">The script to publish.</param>
/// <param name="TestResult">The last test result, checked by the publication gate.</param>
/// <param name="PublishTo">Where a file-based publisher writes the script.</param>
/// <param name="SetAsLatest">
/// Also move the Orchestrator's "latest" tag for this script to the version being published, so
/// scripts that ask for "latest" start using it. Off by default: publishing a version for testing
/// must not change what is live.
/// </param>
public sealed record PublicationRequest(
    Script Script,
    ScriptTestResult TestResult,
    string PublishTo,
    bool SetAsLatest = false);
