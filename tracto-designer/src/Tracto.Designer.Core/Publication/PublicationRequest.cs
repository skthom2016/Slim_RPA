namespace Tracto.Designer.Core.Publication;

using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;

public sealed record PublicationRequest(
    Script Script,
    ScriptSignature Signature,
    ScriptTestResult TestResult,
    string PublishTo);
