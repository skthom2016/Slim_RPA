using System.Text.Json;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.OutputBindings;
using Tracto.TerminalRuntime.Domain.OutputBindings;

namespace Tracto.TerminalRuntime.Infrastructure.OutputBindings;

/// <summary>
/// Parses a step's captured stdout as a top-level JSON object and extracts
/// the values for each declared binding name.
///
/// Contract (AC-1):
/// <list type="bullet">
///   <item>The raw JSON value string is passed unchanged to the caller — the runtime
///         performs no numeric comparison or domain decision on it.</item>
///   <item>Non-JSON stdout or a missing declared key yields a validation error.</item>
/// </list>
/// </summary>
public sealed class OutputBindingResolver : IOutputBindingResolver
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <inheritdoc/>
    public Result<IReadOnlyList<StepOutputBinding>, Error> Resolve(
        string capturedStdout,
        IReadOnlyList<string> declaredBindingNames)
    {
        ArgumentNullException.ThrowIfNull(capturedStdout);
        ArgumentNullException.ThrowIfNull(declaredBindingNames);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(capturedStdout, ParseOptions);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<StepOutputBinding>, Error>.Fail(
                Error.Validation(
                    "terminal.outputBinding.parseError",
                    $"Captured stdout is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result<IReadOnlyList<StepOutputBinding>, Error>.Fail(
                    Error.Validation(
                        "terminal.outputBinding.notAnObject",
                        "Captured stdout JSON root must be an object."));
            }

            var bindings = new List<StepOutputBinding>(declaredBindingNames.Count);

            foreach (var name in declaredBindingNames)
            {
                if (!document.RootElement.TryGetProperty(name, out var element))
                {
                    return Result<IReadOnlyList<StepOutputBinding>, Error>.Fail(
                        Error.Validation(
                            "terminal.outputBinding.missingKey",
                            $"Declared output binding '{name}' was not present in the captured stdout JSON."));
                }

                // Serialize the raw element back to a string with no interpretation.
                var rawValue = element.GetRawText();
                bindings.Add(new StepOutputBinding(name, rawValue));
            }

            return Result<IReadOnlyList<StepOutputBinding>, Error>.Ok(bindings);
        }
    }
}
