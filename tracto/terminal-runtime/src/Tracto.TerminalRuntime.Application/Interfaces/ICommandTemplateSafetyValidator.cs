using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.Interfaces;

/// <summary>
/// Validates a command template text against the <see cref="CommandTemplateSafetyPolicy"/>
/// before any process is launched.
///
/// <para>
/// The validator enforces two independent CEC v1.0.1 invariants:
/// <list type="number">
///   <item>
///     <b>No business-logic predicates</b> (invariant #7): patterns such as
///     <c>if ($amount -gt 10000)</c> are forbidden in command templates.
///   </item>
///   <item>
///     <b>No direct network-tool calls</b> (invariant #5): shell commands that
///     reach third-party APIs or databases directly (curl, wget, Invoke-WebRequest,
///     Invoke-RestMethod, SQL clients) are forbidden — all external calls must go
///     through <c>service.invoke</c>.
///   </item>
/// </list>
/// </para>
/// </summary>
public interface ICommandTemplateSafetyValidator
{
    /// <summary>
    /// Validates <paramref name="templateText"/> against <paramref name="policy"/>.
    /// </summary>
    /// <param name="templateText">The raw command template text to inspect.</param>
    /// <param name="policy">The safety policy that declares forbidden patterns.</param>
    /// <returns>
    /// <see cref="Result{T,TError}.Ok"/> with the original <paramref name="templateText"/>
    /// when the template is safe; <see cref="Result{T,TError}.Fail"/> with an
    /// <see cref="ErrorKind.Validation"/> error otherwise.
    /// </returns>
    Result<string, Error> Validate(string templateText, CommandTemplateSafetyPolicy policy);

    /// <summary>
    /// Convenience overload that uses <see cref="CommandTemplateSafetyPolicy.Default"/>.
    /// </summary>
    Result<string, Error> Validate(string templateText);
}
