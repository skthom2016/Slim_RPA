using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.OutputBindings;

namespace Tracto.TerminalRuntime.Application.OutputBindings;

/// <summary>
/// Resolves declared output bindings from a step's captured stdout JSON.
/// The runtime performs no numeric comparison or domain decision on the JSON values;
/// they are passed unchanged to the <c>service.invoke</c> call (AC-1).
/// </summary>
public interface IOutputBindingResolver
{
    /// <summary>
    /// Parses <paramref name="capturedStdout"/> as a JSON object and extracts values
    /// for each name listed in <paramref name="declaredBindingNames"/>.
    /// </summary>
    /// <param name="capturedStdout">
    ///   The raw captured stdout from a step execution. Expected to be a JSON object
    ///   at the top level. Non-JSON or missing keys produce a validation error.
    /// </param>
    /// <param name="declaredBindingNames">
    ///   The binding names declared in the step's output-bindings configuration.
    /// </param>
    /// <returns>
    ///   A collection of <see cref="StepOutputBinding"/> — one per resolved name —
    ///   or a validation error when the stdout cannot be parsed or a declared name
    ///   is absent from the JSON object.
    /// </returns>
    Result<IReadOnlyList<StepOutputBinding>, Error> Resolve(
        string capturedStdout,
        IReadOnlyList<string> declaredBindingNames);
}
