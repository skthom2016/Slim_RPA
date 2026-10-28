using FluentAssertions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.Security;

namespace Tracto.TerminalRuntime.Unit.Tests;

/// <summary>
/// Unit tests for <see cref="CommandTemplateSafetyValidator"/> covering TER-US-009:
/// "No Business Logic in Commands and Network Bypass Prevention".
///
/// Acceptance Criteria addressed:
///   AC-1  Business-predicate template → rejected by validator before any process launches.
///   AC-2  Network-tool template (curl/wget/Invoke-WebRequest/Invoke-RestMethod/SQL)
///         → rejected before any process launches; no HTTP request emitted.
///   AC-3  Approved template passes; same operation as shell-based HTTP command fails.
///   AC-4  Dynamically assembled shell string from API response → rejected.
///   AC-5  Structured output passed to service.invoke unchanged; no domain calculation.
/// </summary>
public sealed class CommandTemplateSafetyValidatorTests
{
    private readonly CommandTemplateSafetyValidator _sut = new();

    // ── AC-1: Business-predicate templates ──────────────────────────────────────

    [Theory]
    [InlineData("if ($amount -gt 10000) { Write-Host 'High value' }")]
    [InlineData("if ($total -lt 500) { exit 1 }")]
    [InlineData("if ($balance -ge 100000) { Send-Alert }")]
    [InlineData("[ $value -gt 99 ] && echo high")]
    [InlineData("[ $amount -lt 1000 ] && echo low")]
    public void Validate_BusinessPredicateTemplate_IsRejected(string template)
    {
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue(
            because: $"'{template}' contains a business-logic predicate");
        result.Error.Kind.Should().Be(ErrorKind.Validation,
            because: "AC-1 mandates a Validation error");
        result.Error.DetailCode.Should().Be("terminal.businessLogicInTemplate",
            because: "AC-1 mandates detailCode=terminal.businessLogicInTemplate");
    }

    [Fact]
    public void Validate_BusinessPredicateTemplate_DefaultPolicy_IsRejected()
    {
        // Uses the default convenience overload — no explicit policy needed.
        const string template = "if ($amount -gt 10000) { Write-Host 'Large transfer' }";

        var result = _sut.Validate(template);

        result.IsFailure.Should().BeTrue(
            because: "the default policy forbids numeric threshold comparisons");
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.businessLogicInTemplate");
    }

    // ── AC-2: Network-tool templates ─────────────────────────────────────────────

    [Theory]
    [InlineData("curl -X POST https://api.third-party.com/endpoint -d '{}'")]
    [InlineData("wget -O - https://example.com/data")]
    [InlineData("Invoke-WebRequest -Uri 'https://api.example.com' -Method POST")]
    [InlineData("Invoke-RestMethod -Uri 'https://payments.internal/v1/charge' -Method GET")]
    [InlineData("sqlcmd -S server -Q \"SELECT * FROM Orders\"")]
    [InlineData("mysql -u root -p mydb -e 'SELECT amount FROM transactions'")]
    public void Validate_NetworkToolTemplate_IsRejectedBeforeProcessLaunch(string template)
    {
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue(
            because: $"'{template}' contains a forbidden network-tool invocation");
        result.Error.Kind.Should().Be(ErrorKind.Validation,
            because: "AC-2 mandates a Validation error");
        result.Error.DetailCode.Should().Be("terminal.networkBypassInTemplate",
            because: "AC-2 mandates detailCode=terminal.networkBypassInTemplate");
    }

    [Fact]
    public void Validate_CurlInTemplate_IsRejected()
    {
        // Explicit singular AC-2 test that also documents the intent: no HTTP is emitted.
        // The validator runs synchronously before any Process.Start is called.
        const string template = "curl -s https://api.third-party.com/data | jq .result";

        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.networkBypassInTemplate");
    }

    // ── AC-3: Approved template passes; shell-based HTTP equivalent fails ────────

    [Theory]
    [InlineData("Write-Host 'Hello World'")]
    [InlineData("Get-ChildItem C:\\work\\output")]
    [InlineData("python process_data.py --input data.json --output result.json")]
    [InlineData("dotnet run --project MyApp.csproj")]
    [InlineData("node index.js")]
    public void Validate_ApprovedTemplate_Passes(string template)
    {
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsSuccess.Should().BeTrue(
            because: $"'{template}' is a safe, static command template");
        result.Value.Should().Be(template,
            because: "AC-5: the template text is passed unchanged on success");
    }

    [Fact]
    public void Validate_ApprovedServiceInvokeStyle_Passes_WhileCurlFails()
    {
        // AC-3: a service.invoke-style template (just a command name, no HTTP client)
        // passes; the shell-based HTTP equivalent is rejected.
        const string approved = "service.invoke PaymentService.Charge --amount $AMOUNT";
        const string forbidden = "curl -X POST https://payments.internal/charge -d '{\"amount\":$AMOUNT}'";

        var approvedResult = _sut.Validate(approved, CommandTemplateSafetyPolicy.Default);
        var forbiddenResult = _sut.Validate(forbidden, CommandTemplateSafetyPolicy.Default);

        approvedResult.IsSuccess.Should().BeTrue(
            because: "service.invoke template contains no forbidden patterns");
        forbiddenResult.IsFailure.Should().BeTrue(
            because: "curl template bypasses service.invoke and must be rejected");
    }

    // ── AC-4: Dynamic shell string derived from API response ─────────────────────

    [Theory]
    [InlineData("Write-Host ${apiResponse.value}")]
    [InlineData("exec #{injected_command}")]
    [InlineData("run {{dynamic_param}}")]
    [InlineData("process \"key\":\"dynamically-injected-value\" --flag")]
    public void Validate_DynamicShellStringFromApi_IsRejected(string template)
    {
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue(
            because: $"'{template}' appears to contain dynamically injected content");
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.dynamicCommandRejected",
            because: "AC-4 mandates detailCode=terminal.dynamicCommandRejected");
    }

    // ── AC-5: Structured output passed unchanged (validator does not modify) ──────

    [Fact]
    public void Validate_StructuredOutputTemplate_PassesUnmodified()
    {
        // The validator must not alter the template — it is returned verbatim on success.
        const string template = "Get-Process | ConvertTo-Json -Depth 2";

        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(template,
            because: "AC-5: runtime makes no modification to the template text");
    }

    // ── Policy customization ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllowAllPolicy_PermitsAnyTemplate()
    {
        // With an empty policy (AllowAll) even normally-forbidden strings pass.
        const string template = "curl https://example.com && if ($x -gt 9999) { echo done }";

        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.AllowAll);

        result.IsSuccess.Should().BeTrue(
            because: "AllowAll policy has no forbidden patterns");
    }

    [Fact]
    public void Validate_CustomForbiddenPattern_RejectsMatchingTemplate()
    {
        var policy = new CommandTemplateSafetyPolicy(
            ForbiddenPatterns: [@"\bDROP\s+TABLE\b"],
            ForbiddenNetworkTools: []);

        const string template = "sqlcmd -Q 'DROP TABLE Users'";

        var result = _sut.Validate(template, policy);

        result.IsFailure.Should().BeTrue(
            because: "the custom policy forbids DROP TABLE");
        result.Error.DetailCode.Should().Be("terminal.businessLogicInTemplate");
    }

    // ── False-positive regression guards ─────────────────────────────────────────

    [Theory]
    [InlineData("git fetch origin main")]
    [InlineData("git fetch --all --prune")]
    [InlineData("svn update --revision HEAD")]
    public void Validate_ScmFetchCommands_AreNotRejectedByDefaultPolicy(string template)
    {
        // Regression guard: \bfetch\b was replaced with \bfetch\s*\( to prevent
        // legitimate SCM operations from being blocked (MAJOR review finding #2).
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsSuccess.Should().BeTrue(
            because: $"'{template}' is a legitimate SCM command with no network-bypass intent");
    }

    [Theory]
    [InlineData("fetch('https://api.example.com/data')")]
    [InlineData("await fetch('https://payments.internal/charge')")]
    public void Validate_FetchCallSyntax_IsRejectedByDefaultPolicy(string template)
    {
        // The narrowed pattern still catches the actual threat: fetch() call syntax.
        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue(
            because: $"'{template}' uses JS/Python fetch() call syntax to bypass service.invoke");
        result.Error.DetailCode.Should().Be("terminal.networkBypassInTemplate");
    }

    // ── Error message quality ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_BusinessPredicate_ErrorMessageDoesNotContainSecrets()
    {
        // The error message must not echo the full template back — it should only
        // reference the matched fragment to avoid leaking template content.
        const string template = "if ($secretApiKey -gt 10000) { Invoke-RestMethod ... }";

        var result = _sut.Validate(template, CommandTemplateSafetyPolicy.Default);

        result.IsFailure.Should().BeTrue();
        // The error references what was matched, not the full template string.
        result.Error.Message.Should().NotContain("secretApiKey",
            because: "error messages must not echo potentially sensitive template text verbatim");
    }
}
