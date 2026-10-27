namespace Tracto.Desktop.Session;

/// <summary>
/// Determines whether the current environment is eligible for GUI input by combining
/// session state and display profile checks.
/// </summary>
public sealed class SessionEligibilityChecker
{
    private readonly ISessionStateProvider _sessionProvider;
    private readonly IDisplayProfileProvider _displayProvider;
    private readonly DisplayProfile _baseline;

    /// <param name="sessionProvider">Provides the current Windows session state.</param>
    /// <param name="displayProvider">Provides the current display profile.</param>
    /// <param name="baseline">The certified display profile that the current environment must match.</param>
    public SessionEligibilityChecker(
        ISessionStateProvider sessionProvider,
        IDisplayProfileProvider displayProvider,
        DisplayProfile baseline)
    {
        _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        _displayProvider = displayProvider ?? throw new ArgumentNullException(nameof(displayProvider));
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
    }

    /// <summary>
    /// Checks whether the session is eligible for GUI input.
    /// </summary>
    /// <returns>
    /// A <see cref="SessionEligibilityResult"/> describing the outcome.
    /// When <see cref="SessionEligibilityResult.IsEligible"/> is false, <see cref="SessionEligibilityResult.ErrorCategory"/>
    /// and <see cref="SessionEligibilityResult.DetailCode"/> are set to CEC v1.0.1 values.
    /// </returns>
    public SessionEligibilityResult CheckEligibility()
    {
        var state = _sessionProvider.GetCurrentState();

        if (state != SessionState.Interactive)
        {
            return new SessionEligibilityResult(
                IsEligible: false,
                State: state,
                Reason: $"Session is not interactive: {state}.",
                ErrorCategory: "SESSION_UNAVAILABLE",
                DetailCode: "desktop.sessionUnavailable");
        }

        var current = _displayProvider.GetCurrentProfile();
        if (current != _baseline)
        {
            return new SessionEligibilityResult(
                IsEligible: false,
                State: state,
                Reason: $"Display profile mismatch. Baseline={_baseline}, Current={current}.",
                ErrorCategory: "ENVIRONMENT",
                DetailCode: "desktop.displayProfileChanged");
        }

        return new SessionEligibilityResult(
            IsEligible: true,
            State: state,
            Reason: "Session is eligible for GUI input.",
            ErrorCategory: null,
            DetailCode: null);
    }
}
