namespace Tracto.Designer.Core.Privacy;

using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

public sealed class PrivacyFilter(PrivacySettings settings) : IPrivacyFilter
{
    public IEnumerable<TranscriptEntry> FilterTranscript(IEnumerable<TranscriptEntry> entries)
    {
        foreach (var entry in entries)
        {
            var evt = entry.SourceEvent;
            string? narration = entry.NarrationText;

            // Redact password-field values in the raw event so the value never
            // flows through to callers unmasked.  The redacted marker is written
            // into SourceEvent.Value — the previous code computed the redacted
            // string but never applied it to the output record (dead-code bug).
            if (settings.HasFlag(PrivacyPolicy.RedactPasswords) && evt.IsPasswordField)
                evt = evt with { Value = SensitivePatternDetector.Redacted };

            // Scrub narration of PII
            if (narration is not null)
                narration = SensitivePatternDetector.Scrub(narration, settings.Policy);

            // Yield a new (possibly scrubbed) entry
            if (!ReferenceEquals(evt, entry.SourceEvent) || narration != entry.NarrationText)
                yield return entry with { SourceEvent = evt, NarrationText = narration };
            else
                yield return entry;
        }
    }

    public Script FilterScript(Script script)
    {
        var filtered = new Script
        {
            Id = script.Id,
            Name = script.Name,
            Version = script.Version,
            TargetRuntime = script.TargetRuntime
        };
        foreach (var step in script.Steps)
        {
            var filteredStep = new ScriptStep
            {
                StepNumber  = step.StepNumber,
                Description = SensitivePatternDetector.Scrub(step.Description, settings.Policy),
                Action      = step.Action,
                Condition   = step.Condition
            };
            foreach (var (k, v) in step.Parameters)
            {
                var fv = v is null ? null : SensitivePatternDetector.Scrub(v, settings.Policy);
                filteredStep.Parameters[k] = fv;
            }
            filtered.Steps.Add(filteredStep);
        }
        return filtered;
    }
}
