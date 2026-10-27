#nullable enable

using System;
using System.Collections.Generic;

namespace Tracto.Contract.Validation;

// Loaded from schema-version-catalogue.json at startup.
public record SchemaVersionCatalogue(
    int CurrentMajor,
    string CurrentVersion,
    IReadOnlyList<int> SupportedMajors,
    int? PreviousMajor)
{
    public bool IsVersionSupported(string schemaVersion)
    {
        if (!Version.TryParse(schemaVersion, out var v)) return false;
        return SupportedMajors.Contains(v.Major);
    }
}
