namespace Tracto.Contract.Execution;

public static class UnknownReasonCodes
{
    public const string ActionCommitUncertain = "action.commitUncertain";
    public const string PostconditionIrrevocableUncertain = "postcondition.irrevocableUncertain";
    public const string LeaseExpiredWithActiveStep = "leaseExpired.activeStepUncertain";
    public const string ReconcileDispositionUnknown = "reconcile.dispositionUnknown";
    public const string TransportLossAfterDispatch = "transport.lossAfterDispatch";
}
