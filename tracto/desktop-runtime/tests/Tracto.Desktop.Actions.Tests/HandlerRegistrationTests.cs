using FluentAssertions;
using Tracto.Desktop.Actions;
using Xunit;

namespace Tracto.Desktop.Actions.Tests;

public class HandlerRegistrationTests
{
    private static string HostProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Tracto.Desktop.Host", "Program.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("src/Tracto.Desktop.Host/Program.cs not found above the test output folder");
    }

    private static IEnumerable<Type> HandlerTypes() =>
        typeof(IActionHandler).Assembly.GetTypes()
            .Where(t => typeof(IActionHandler).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

    // A handler that is written but not registered fails at run time with "UnknownAction:<name>".
    [Fact]
    public void Every_handler_class_is_registered_in_the_Host()
    {
        var source = HostProgramSource();
        var missing = HandlerTypes()
            .Where(t => !source.Contains($"IActionHandler, {t.Name}>"))
            .Select(t => t.Name)
            .ToList();

        missing.Should().BeEmpty("these handlers are not registered in Tracto.Desktop.Host/Program.cs");
    }

    [Fact]
    public void Action_names_are_unique()
    {
        var duplicates = HandlerTypes()
            .GroupBy(SourceActionName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        duplicates.Should().BeEmpty();
    }

    // Reads `ActionName => "x"` from the handler's source so no constructor arguments are needed.
    private static string SourceActionName(Type t)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, "src", "Tracto.Desktop.Actions", "Handlers", t.Name + ".cs");
            if (File.Exists(file))
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), "ActionName\\s*=>\\s*\"([^\"]+)\"");
                if (m.Success) return m.Groups[1].Value;
            }
            dir = dir.Parent;
        }
        return t.Name;
    }
}
