namespace Tracto.Designer.Core.Testing;

public sealed record StepTestResult(int StepNumber, bool Passed, string Message, TimeSpan Duration);
