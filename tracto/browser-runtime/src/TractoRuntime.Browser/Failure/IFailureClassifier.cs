using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Failure;

public interface IFailureClassifier
{
    /// <summary>
    /// Classifies an exception into a CEC errorCategory.
    /// State-dependent: FAILED when run state is deterministically known, UNKNOWN otherwise.
    /// </summary>
    CecError Classify(Exception ex, bool stateKnown);
}
