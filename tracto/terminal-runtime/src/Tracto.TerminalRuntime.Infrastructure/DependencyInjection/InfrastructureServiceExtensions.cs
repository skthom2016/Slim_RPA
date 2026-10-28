using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Arguments;
using Tracto.TerminalRuntime.Application.Audit;
using Tracto.TerminalRuntime.Application.Commands;
using Tracto.TerminalRuntime.Application.Identity;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Application.OutputBindings;
using Tracto.TerminalRuntime.Application.PowerShell;
using Tracto.TerminalRuntime.Application.Execution;
using Tracto.TerminalRuntime.Application.Redaction;
using Tracto.TerminalRuntime.Application.Shells;
using Tracto.TerminalRuntime.Application.Stdin;
using Tracto.TerminalRuntime.Application.Wsl;
using Tracto.TerminalRuntime.Infrastructure.Arguments;
using Tracto.TerminalRuntime.Infrastructure.Audit;
using Tracto.TerminalRuntime.Infrastructure.Commands;
using Tracto.TerminalRuntime.Infrastructure.OutputBindings;
using Tracto.TerminalRuntime.Infrastructure.PowerShell;
using Tracto.TerminalRuntime.Infrastructure.Identity;
using Tracto.TerminalRuntime.Infrastructure.Execution;
using Tracto.TerminalRuntime.Infrastructure.Redaction;
using Tracto.TerminalRuntime.Infrastructure.Security;
using Tracto.TerminalRuntime.Infrastructure.Shells;
using Tracto.TerminalRuntime.Infrastructure.Stdin;
using Tracto.TerminalRuntime.Infrastructure.Wsl;

namespace Tracto.TerminalRuntime.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods to wire Infrastructure services into the DI container.
/// Call from the Host layer only.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>Registers all Infrastructure-layer services.</summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IShellDetector, WindowsShellDetector>();

        // TER-US-020: Signed Artifact Validation
        services
            .AddOptions<ArtifactSignatureSettings>()
            .BindConfiguration(ArtifactSignatureSettings.SectionName);

        services.AddSingleton<IArtifactValidator, ArtifactValidator>();

        // TER-US-002: Execution Identity and Privilege Enforcement
        services.AddSingleton<IIdentityVerifier, WindowsIdentityVerifier>();

        // TER-US-003: Working Directory Policy and Environment Variable Handling
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<ISecretStore, StubSecretStore>();
        services.AddSingleton<IEnvironmentBuilder, EnvironmentBuilder>();

        // TER-US-009: No Business Logic in Commands and Network Bypass Prevention
        services.AddSingleton<ICommandTemplateSafetyValidator, CommandTemplateSafetyValidator>();

        // TER-US-008: WSL Bash Script Invocation and Distro Isolation
        services.AddSingleton<IWslCommandValidator, WslCommandValidator>();

        // TER-US-004: Process Creation with Typed Argument Binding
        services.AddSingleton<IArgumentValidator, ArgumentValidator>();
        services.AddSingleton<BuildProcessStartHandler>();
        services.AddSingleton<ProcessStartSpecBuilder>();

        // TER-US-007: PowerShell Safe Execution Policy
        services
            .AddOptions<PowerShellPolicySettings>()
            .BindConfiguration(PowerShellPolicySettings.SectionName);

        services.AddSingleton<IPowerShellPolicyValidator, PowerShellPolicyValidator>();

        // TER-US-006: Command Allowlist Enforcement and Integrity Verification
        services
            .AddOptions<CommandRegistrySettings>()
            .BindConfiguration(CommandRegistrySettings.SectionName);

        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<ICommandHashVerifier, Sha256Verifier>();
        services.AddSingleton<ValidateCommandRefHandler>();

        // TER-US-010: Idempotency Classification and Command Audit
        services.AddSingleton<ICommandAuditLog, JsonCommandAuditLog>();
        services.AddSingleton<AuditCommandExecutionHandler>();

        // TER-US-014: Output Bindings and Approved Result Artifacts
        services
            .AddOptions<StorageRootSettings>()
            .BindConfiguration(StorageRootSettings.SectionName);

        services
            .AddOptions<EnvAllowlistSettings>()
            .BindConfiguration(EnvAllowlistSettings.SectionName);

        services.AddSingleton<IOutputBindingResolver, OutputBindingResolver>();
        services.AddSingleton<IFileExistsAction, FileExistsAction>();
        services.AddSingleton<IEnvInspectAction, EnvInspectAction>();

        // TER-US-012: Bounded Stdin and Allowlisted Prompt Matching
        services.AddSingleton<IStdinWriter, StdinWriter>();
        services.AddSingleton<IPromptMatcher, PromptMatcher>();

        // TER-US-013: Output Redaction and Secret Protection
        // IOutputRedactor must be registered before ITerminalProcessRunner because the
        // runner depends on it.  Co-located here so the full infrastructure layer is
        // self-contained and no caller can accidentally omit the registration.
        services.AddSingleton<IOutputRedactor, OutputRedactor>();
        services.AddSingleton<ITerminalProcessRunner, TerminalProcessRunner>();

        return services;
    }
}
