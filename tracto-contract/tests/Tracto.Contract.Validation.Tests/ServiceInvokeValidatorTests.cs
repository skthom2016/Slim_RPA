#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Tests for <see cref="ServiceInvokeValidator"/> (C4 APPROVED).
/// Uses a <see cref="ServiceRegistry"/> built from a known in-memory list.
/// Uses a shared <see cref="SchemaTestFixture"/> for the real <see cref="SchemaValidator"/>.
/// No mocking; no file I/O in test bodies.
/// </summary>
public sealed class ServiceInvokeValidatorTests : IClassFixture<SchemaTestFixture>
{
    private readonly ServiceInvokeValidator _sut;

    public ServiceInvokeValidatorTests(SchemaTestFixture fixture)
    {
        // Build the service registry from a known in-memory list — no file loading.
        var registry = new ServiceRegistry(new[]
        {
            // crm.create-contact — allows TRANSIENT_SAFE and IDEMPOTENT retries
            new ServiceRegistryEntry(
                ServiceRef:            "crm",
                OperationRef:          "create-contact",
                BaseUrl:               "https://api.crm.internal/contacts",
                HttpMethod:            "POST",
                ResponseSchemaVersion: "1.0",
                AuthPolicy:            "bearer",
                RequiresTls:           true,
                AllowedRetryClasses:   new[] { "TRANSIENT_SAFE", "IDEMPOTENT" }),

            // crm.get-contact — allows any retry class (null list → no restriction checked)
            new ServiceRegistryEntry(
                ServiceRef:            "crm",
                OperationRef:          "get-contact",
                BaseUrl:               "https://api.crm.internal/contacts/{id}",
                HttpMethod:            "GET",
                ResponseSchemaVersion: "1.0",
                AuthPolicy:            "bearer",
                RequiresTls:           true,
                AllowedRetryClasses:   null),

            // billing.charge — ONCE_ONLY only (idempotent-sensitive)
            new ServiceRegistryEntry(
                ServiceRef:            "billing",
                OperationRef:          "charge",
                BaseUrl:               "https://api.billing.internal/charge",
                HttpMethod:            "POST",
                ResponseSchemaVersion: "1.0",
                AuthPolicy:            "bearer",
                RequiresTls:           true,
                AllowedRetryClasses:   new[] { "ONCE_ONLY" }),
        });

        _sut = new ServiceInvokeValidator(registry, fixture.SchemaValidator);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ServiceInvokeJson(
        string serviceRef    = "crm",
        string operationRef  = "create-contact",
        string? retryClass   = null)
    {
        var rc = retryClass is null ? string.Empty : $@",""retryClass"":""{retryClass}""";
        return $$"""
            {
              "serviceRef":   "{{serviceRef}}",
              "operationRef": "{{operationRef}}"
              {{rc}}
            }
            """;
    }

    // ── TC1: Valid service-invoke for registered operation passes ─────────────

    [Fact]
    public async Task ValidServiceInvoke_ForRegisteredOperation_Passes()
    {
        var json   = ServiceInvokeJson("crm", "create-contact");
        var result = await _sut.ValidateAsync(json);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── TC2: Unregistered serviceRef fails with UNREGISTERED_SERVICE_OPERATION ─

    [Fact]
    public async Task UnregisteredServiceRef_FailsWithUnregisteredServiceOperation()
    {
        var json   = ServiceInvokeJson("payroll", "run-payroll");
        var result = await _sut.ValidateAsync(json);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNREGISTERED_SERVICE_OPERATION");
    }

    // ── TC3: Unregistered operationRef for known service fails ────────────────

    [Fact]
    public async Task UnregisteredOperationRef_ForKnownService_FailsWithUnregisteredServiceOperation()
    {
        var json   = ServiceInvokeJson("crm", "delete-contact");   // crm is known, delete-contact is not
        var result = await _sut.ValidateAsync(json);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNREGISTERED_SERVICE_OPERATION");
    }

    // ── TC4: retryClass not in allowedRetryClasses fails with DISALLOWED_RETRY_CLASS ─

    [Fact]
    public async Task DisallowedRetryClass_FailsWithDisallowedRetryClassDiagnostic()
    {
        // billing.charge only allows ONCE_ONLY; TRANSIENT_SAFE is disallowed.
        var json   = ServiceInvokeJson("billing", "charge", retryClass: "TRANSIENT_SAFE");
        var result = await _sut.ValidateAsync(json);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "DISALLOWED_RETRY_CLASS");
    }

    // ── TC5: retryClass in allowedRetryClasses passes ────────────────────────

    [Fact]
    public async Task AllowedRetryClass_Passes()
    {
        // crm.create-contact allows TRANSIENT_SAFE.
        var json   = ServiceInvokeJson("crm", "create-contact", retryClass: "TRANSIENT_SAFE");
        var result = await _sut.ValidateAsync(json);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == "DISALLOWED_RETRY_CLASS");
    }
}
