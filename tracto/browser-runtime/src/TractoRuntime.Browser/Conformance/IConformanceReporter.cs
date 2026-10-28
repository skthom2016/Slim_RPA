namespace TractoRuntime.Browser.Conformance;

public interface IConformanceReporter
{
    Task ReportConformanceAsync(string workerId, string runtimeVersion, CancellationToken ct);
}
