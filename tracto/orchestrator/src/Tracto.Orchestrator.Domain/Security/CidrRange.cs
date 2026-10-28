#nullable enable
using System.Net;
using System.Net.Sockets;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Domain.Security;

/// <summary>
/// Value object representing an IPv4 or IPv6 CIDR range (e.g. "10.0.0.0/8", "::1/128").
/// Parsing is pure .NET — zero EF/ASP references.
/// Used by <c>AdminIpAllowlistMiddleware</c> to decide whether a source IP is permitted.
/// </summary>
public sealed class CidrRange
{
    private readonly IPAddress _networkAddress;
    private readonly IPAddress _maskAddress;
    private readonly int _prefixLength;
    private readonly AddressFamily _family;

    private CidrRange(IPAddress networkAddress, IPAddress maskAddress, int prefixLength)
    {
        _networkAddress = networkAddress;
        _maskAddress    = maskAddress;
        _prefixLength   = prefixLength;
        _family         = networkAddress.AddressFamily;
    }

    /// <summary>The original CIDR notation string supplied at parse time.</summary>
    public string Notation { get; private init; } = string.Empty;

    // ── Factory ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse a CIDR string such as "192.168.0.0/16" or "::1/128".
    /// Returns a failure <see cref="Result{T,E}"/> on invalid input rather than throwing.
    /// </summary>
    public static Result<CidrRange, Error> TryParse(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            return Error.Validation("CIDR string must not be null or empty.");

        int slashPos = cidr.LastIndexOf('/');
        if (slashPos < 0)
            return Error.Validation($"CIDR string '{cidr}' is missing the prefix-length suffix (e.g. /24).");

        // Trim whitespace from both parts so " 10.0.0.0 / 8 " is accepted cleanly.
        string ipPart     = cidr[..slashPos].Trim();
        string prefixPart = cidr[(slashPos + 1)..].Trim();

        if (!IPAddress.TryParse(ipPart, out IPAddress? baseAddress))
            return Error.Validation($"'{ipPart}' is not a valid IP address.");

        if (!int.TryParse(prefixPart, out int prefix))
            return Error.Validation($"'{prefixPart}' is not a valid prefix length.");

        int maxPrefix = baseAddress.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix)
            return Error.Validation($"Prefix length {prefix} is out of range for {baseAddress.AddressFamily}.");

        byte[] maskBytes  = BuildMaskBytes(baseAddress.AddressFamily, prefix);
        byte[] addrBytes  = baseAddress.GetAddressBytes();
        byte[] netBytes   = new byte[addrBytes.Length];
        for (int i = 0; i < addrBytes.Length; i++)
            netBytes[i] = (byte)(addrBytes[i] & maskBytes[i]);

        return new CidrRange(
            new IPAddress(netBytes),
            new IPAddress(maskBytes),
            prefix)
        {
            Notation = cidr,
        };
    }

    // ── Membership test ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if <paramref name="address"/> falls within this CIDR range.
    /// IPv4-mapped IPv6 addresses (::ffff:x.x.x.x) are unwrapped to IPv4 before the test.
    /// </summary>
    public bool Contains(IPAddress address)
    {
        // Normalise IPv4-mapped addresses so callers need not handle both families.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != _family)
            return false;

        byte[] addrBytes = address.GetAddressBytes();
        byte[] maskBytes = _maskAddress.GetAddressBytes();
        byte[] netBytes  = _networkAddress.GetAddressBytes();

        for (int i = 0; i < addrBytes.Length; i++)
        {
            if ((addrBytes[i] & maskBytes[i]) != netBytes[i])
                return false;
        }

        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] BuildMaskBytes(AddressFamily family, int prefix)
    {
        int byteCount = family == AddressFamily.InterNetworkV6 ? 16 : 4;
        byte[] mask   = new byte[byteCount];

        int fullBytes = prefix / 8;
        int remainder = prefix % 8;

        for (int i = 0; i < fullBytes && i < byteCount; i++)
            mask[i] = 0xFF;

        if (fullBytes < byteCount && remainder > 0)
            mask[fullBytes] = (byte)(0xFF << (8 - remainder));

        return mask;
    }

    public override string ToString() => Notation;
}
