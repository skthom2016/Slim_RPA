namespace Tracto.Designer.Core.IsolatedMode;

public sealed record IsolatedViolation(string Code, string Message, int? StepNumber = null);
