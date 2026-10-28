using System.Security.Cryptography;
using System.Text;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Mode;

namespace TractoRuntime.Browser.Capabilities;

/// <summary>
/// Production implementation of <see cref="ICapabilityAdvertiser"/>.
/// Composes the capability advertisement from the validated mode config and runtime metadata.
/// BRW-US-002 AC#4, BRW-006.
/// </summary>
public sealed class CapabilityAdvertiser(IRuntimeInfo runtimeInfo) : ICapabilityAdvertiser
{
    public async Task<WorkerCapabilityPayload> BuildPayloadAsync(
        string workerId,
        ExecutionModeConfig modeConfig,
        CancellationToken ct)
    {
        var (engine, version) = await runtimeInfo.GetBrowserInfoAsync(ct);

        string modeToken = modeConfig.Mode is BrowserMode.Headed ? "headed" : "headless";
        IReadOnlyList<string> capabilities = BuildCapabilities(modeConfig, modeToken, engine);
        string digest = ComputeDigest(capabilities, engine, version);

        return new WorkerCapabilityPayload
        {
            WorkerId = workerId,
            RuntimeType = "browser",
            Mode = modeToken,
            // InteractiveSessionId is populated only for headed mode (AC#4).
            InteractiveSessionId = modeConfig.Mode is BrowserMode.Headed
                ? modeConfig.InteractiveSessionId
                : null,
            BrowserEngine = engine,
            BrowserVersion = version,
            Capabilities = capabilities,
            CapabilityDigest = digest
        };
    }

    private static IReadOnlyList<string> BuildCapabilities(
        ExecutionModeConfig config,
        string modeToken,
        string engine)
    {
        List<string> caps =
        [
            $"browser.{modeToken}",
            $"browser.engine.{engine.ToLowerInvariant()}"
        ];

        // Advertise session scope when headed so Orchestrator can route correctly.
        if (config.Mode is BrowserMode.Headed && config.InteractiveSessionId is { } sid)
            caps.Add($"browser.session.{sid}");

        caps.Sort(StringComparer.Ordinal);
        return caps.AsReadOnly();
    }

    /// <summary>
    /// Computes a short integrity hint over the capability tokens, engine, and version.
    /// This is NOT an authentication credential — it is an integrity marker only
    /// (aligned with C4 approval: capabilityDigest = integrity hint only, not auth).
    /// </summary>
    private static string ComputeDigest(IReadOnlyList<string> capabilities, string engine, string version)
    {
        string raw = string.Join("|", capabilities) + "|" + engine + "|" + version;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        // 16 hex chars = 8 bytes = 64-bit prefix — sufficient for an integrity hint.
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
