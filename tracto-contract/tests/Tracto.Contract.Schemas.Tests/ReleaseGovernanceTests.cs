#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Schemas.Tests;

/// <summary>
/// Sprint 6 release governance tests — ST1-15 ACs 01-08.
/// Verifies the release artifacts, schema catalogue, fixture manifests,
/// runtime type taxonomy, and architecture freeze.
/// </summary>
public sealed class ReleaseGovernanceTests
{
    // -----------------------------------------------------------------------
    // Repository-root discovery (walks up from the test binary directory)
    // -----------------------------------------------------------------------
    private static string RepoRoot => FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return AppContext.BaseDirectory;
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC01: Canonical package — schema-version-catalogue.json exists
    //              and contains a contractVersion field
    // -----------------------------------------------------------------------
    [Fact]
    public void SchemaVersionCatalogue_Exists_AndHasContractVersion()
    {
        var path = Path.Combine(RepoRoot, "src", "Tracto.Contract.Schemas", "schema-version-catalogue.json");
        File.Exists(path).Should().BeTrue("schema-version-catalogue.json must exist");
        var json = JsonDocument.Parse(File.ReadAllText(path));
        json.RootElement.TryGetProperty("contractVersion", out _).Should().BeTrue(
            "schema-version-catalogue.json must have a 'contractVersion' field");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC01: All 10 contract artifact schema files exist under schemas/v1/
    // -----------------------------------------------------------------------
    [Fact]
    public void AllSchemaFilesExist()
    {
        var schemaDir = Path.Combine(RepoRoot, "src", "Tracto.Contract.Schemas", "schemas", "v1");
        var expectedSchemas = new[]
        {
            "automation-envelope.schema.json",
            "step-envelope.schema.json",
            "technical-condition.schema.json",
            "target-map.schema.json",
            "service-invoke.schema.json",
            "async-callback.schema.json",
            "telemetry-event.schema.json",
            "pull-lease-protocol.schema.json",
            "run-result.schema.json",
            "worker-registration.schema.json",
        };

        foreach (var s in expectedSchemas)
            File.Exists(Path.Combine(schemaDir, s)).Should().BeTrue($"{s} must exist under schemas/v1/");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC02: At least one fixture file exists for each of CF-01..CF-10
    // -----------------------------------------------------------------------
    [Fact]
    public void ConformanceFixtures_ExistForAllCFIds()
    {
        var fixtureDir = Path.Combine(RepoRoot, "src", "Tracto.Conformance.Fixtures", "fixtures");
        for (int i = 1; i <= 10; i++)
        {
            var id = $"CF-{i:D2}";
            File.Exists(Path.Combine(fixtureDir, id + ".json"))
                .Should().BeTrue($"{id}.json fixture must exist in fixtures/");
        }
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC03: SchemaVersionCatalogue reports v1 as supported
    // -----------------------------------------------------------------------
    [Fact]
    public void SchemaVersionCatalogue_SupportsMajorOne()
    {
        var catalogue = new SchemaVersionCatalogue(
            CurrentMajor: 1,
            CurrentVersion: "1.0",
            SupportedMajors: new[] { 1 },
            PreviousMajor: null);

        catalogue.IsVersionSupported("1.0").Should().BeTrue(
            because: "schema version 1.0 must be supported in Stage 1");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC04: Breaking-change detection — unsupported schema majors are rejected
    // -----------------------------------------------------------------------
    [Fact]
    public void SchemaVersionCatalogue_RejectsUnsupportedMajor()
    {
        var catalogue = new SchemaVersionCatalogue(
            CurrentMajor: 1,
            CurrentVersion: "1.0",
            SupportedMajors: new[] { 1 },
            PreviousMajor: null);

        catalogue.IsVersionSupported("0.9").Should().BeFalse(
            because: "schema major 0 is not in the supported list");
        catalogue.IsVersionSupported("2.0").Should().BeFalse(
            because: "schema major 2 is not yet published");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC05: Named accountability — conformance-manifest.json exists and
    //              has required ownership fields with non-null values
    // -----------------------------------------------------------------------
    [Fact]
    public void ConformanceManifest_Exists_AndHasOwnershipFields()
    {
        var path = Path.Combine(RepoRoot, "src", "Tracto.Conformance.Fixtures", "conformance-manifest.json");
        File.Exists(path).Should().BeTrue("conformance-manifest.json must exist");

        var json = JsonDocument.Parse(File.ReadAllText(path));

        json.RootElement.TryGetProperty("leadArchitect", out var la)
            .Should().BeTrue("'leadArchitect' must be present");
        json.RootElement.TryGetProperty("hostSupervisorOwner", out var hs)
            .Should().BeTrue("'hostSupervisorOwner' must be present");
        json.RootElement.TryGetProperty("fixtureMaintainer", out var fm)
            .Should().BeTrue("'fixtureMaintainer' must be present");

        // Values must be non-null strings (role placeholder strings are acceptable)
        la.ValueKind.Should().NotBe(JsonValueKind.Null, because: "leadArchitect must not be null");
        hs.ValueKind.Should().NotBe(JsonValueKind.Null, because: "hostSupervisorOwner must not be null");
        fm.ValueKind.Should().NotBe(JsonValueKind.Null, because: "fixtureMaintainer must not be null");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC06: Independent conformance — Desktop, Browser and Terminal are
    //              distinct RuntimeType enum values
    // -----------------------------------------------------------------------
    [Fact]
    public void ThreeRuntimeTypes_AreDistinct()
    {
        var types = Enum.GetValues<RuntimeType>();
        types.Should().Contain(RuntimeType.Desktop);
        types.Should().Contain(RuntimeType.Browser);
        types.Should().Contain(RuntimeType.Terminal);
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC07: Frozen architecture — no fourth component directory exists
    //              under src/ that is not in the approved set
    // -----------------------------------------------------------------------
    [Fact]
    public void NoFourthProductComponent_ExistsInSolution()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        var projects = Directory.GetDirectories(srcDir)
            .Select(Path.GetFileName)
            .ToHashSet();

        var allowed = new HashSet<string?>
        {
            "Tracto.Contract.Models",
            "Tracto.Contract.Schemas",
            "Tracto.Contract.Validation",
            "Tracto.Contract.Execution",
            "Tracto.Contract.Orchestrator",
            "Tracto.Contract.Testing",
            "Tracto.Contract.Telemetry",
            "Tracto.Mock.Orchestrator",
            "Tracto.Mock.BusinessService",
            "Tracto.Conformance.Fixtures",
            "Tracto.Conformance.Runner",
            "Tracto.Reference.Adapter",
        };

        var unexpected = projects.Except(allowed).ToList();
        unexpected.Should().BeEmpty(
            because: "architecture is frozen; no new product component directories may be added under src/");
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC08: No implementation lock-in — ReferenceAdapter implements
    //              IReferenceAdapter (compile-time and runtime check)
    // -----------------------------------------------------------------------
    [Fact]
    public void ReferenceAdapter_DependsOnInterface_NotConcrete()
    {
        typeof(Tracto.Reference.Adapter.ReferenceAdapter)
            .GetInterfaces()
            .Should().Contain(
                typeof(Tracto.Reference.Adapter.IReferenceAdapter),
                because: "ReferenceAdapter must implement IReferenceAdapter " +
                         "and callers must depend on the interface, not the concrete class");
    }
}
