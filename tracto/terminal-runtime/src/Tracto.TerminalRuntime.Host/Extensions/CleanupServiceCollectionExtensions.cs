using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Domain.Cleanup;
using Tracto.TerminalRuntime.Infrastructure.Cleanup;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for TER-US-019: Filesystem Cleanup and Async Service Handoff.
///
/// Registers:
/// <list type="bullet">
///   <item><see cref="IFilesystemCleanup"/> → <see cref="FilesystemCleanup"/></item>
///   <item><see cref="IServiceHandoffHandler"/> → <see cref="ServiceHandoffHandler"/></item>
///   <item><see cref="IStaticContinuationMappingStore"/> → <see cref="InMemoryStaticContinuationMappingStore"/></item>
///   <item><see cref="ICallbackAuthenticator"/> → <see cref="HmacCallbackAuthenticator"/></item>
///   <item><see cref="ReceiveCallbackHandler"/> (transient)</item>
/// </list>
/// </summary>
public static class CleanupServiceCollectionExtensions
{
    /// <summary>
    /// Registers filesystem cleanup and async service handoff services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="continuationMappings">
    ///   Statically configured continuation mappings loaded from appsettings.
    ///   These are immutable at runtime; any dynamic scriptRef from a callback
    ///   payload is refused (CEC v1.0.1 AC-4).
    /// </param>
    /// <param name="callbackSharedSecret">
    ///   Shared HMAC-SHA256 secret used to authenticate inbound service callbacks
    ///   (CEC v1.0.1 AC-3). Must not be empty. The value is never logged or
    ///   persisted (CEC invariant #4).
    /// </param>
    public static IServiceCollection AddFilesystemCleanupServices(
        this IServiceCollection services,
        IEnumerable<StaticContinuationMapping>? continuationMappings = null,
        string callbackSharedSecret = "")
    {
        services.AddSingleton<IFilesystemCleanup, FilesystemCleanup>();
        services.AddTransient<IServiceHandoffHandler, ServiceHandoffHandler>();
        services.AddSingleton<IStaticContinuationMappingStore>(
            _ => new InMemoryStaticContinuationMappingStore(
                continuationMappings ?? []));
        services.AddSingleton<ICallbackAuthenticator>(sp =>
            new HmacCallbackAuthenticator(
                callbackSharedSecret,
                sp.GetRequiredService<ILogger<HmacCallbackAuthenticator>>()));
        services.AddTransient<ReceiveCallbackHandler>();
        return services;
    }
}
