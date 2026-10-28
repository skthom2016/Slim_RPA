namespace TractoRuntime.Browser.Network;

/// <summary>
/// Thrown when a browser navigation or network operation encounters a transport-layer
/// failure (BRE-003 / BRW-US-015 AC#1).
///
/// <para>Recognised by <see cref="Failure.FailureClassifier"/> and mapped to
/// <c>errorCategory=TRANSPORT</c> with the appropriate <c>browser.*</c> detail code.</para>
///
/// <para>Callers that throw this exception have already determined the <see cref="DetailCode"/>
/// from the underlying Playwright or HTTP error message; the classifier uses it directly.</para>
/// </summary>
public sealed class NetworkTransportException(string detailCode, string message)
    : Exception(message)
{
    /// <summary>
    /// The CEC detail code identifying the transport failure kind:
    /// <list type="bullet">
    ///   <item><c>browser.dnsFailure</c>         — DNS resolution failure (ERR_NAME_NOT_RESOLVED).</item>
    ///   <item><c>browser.tlsCertificateError</c> — TLS / certificate error (ERR_CERT_*, ERR_SSL_*).</item>
    ///   <item><c>browser.proxyFailure</c>        — Proxy connection or authentication failure.</item>
    ///   <item><c>browser.networkOffline</c>      — Connection reset, refused, or network offline.</item>
    ///   <item><c>browser.httpError</c>           — HTTP-level protocol error.</item>
    /// </list>
    /// </summary>
    public string DetailCode { get; } = detailCode;
}
