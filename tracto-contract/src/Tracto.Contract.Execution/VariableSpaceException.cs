#nullable enable

using System;

namespace Tracto.Contract.Execution;

public sealed class VariableSpaceException : Exception
{
    public string ErrorDetail { get; }
    public string VarName { get; }

    public VariableSpaceException(string errorDetail, string varName, string message)
        : base(message)
    {
        ErrorDetail = errorDetail;
        VarName = varName;
    }
}

// Error detail codes (matching UnknownReasonCodes style).
public static class VariableErrorCodes
{
    public const string UninitializedRead = "variable.uninitializedRead";
    public const string ForwardRead = "variable.forwardRead";
    public const string UndeclaredReference = "variable.undeclaredReference";
    public const string TypeMismatch = "variable.typeMismatch";
    public const string DuplicateName = "variable.duplicateName";
}
