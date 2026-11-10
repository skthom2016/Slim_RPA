using FluentAssertions;
using TractoRuntime.Browser.Configuration;

namespace TractoRuntime.Browser.Tests.Configuration;

/// <summary>
/// Tests for the <c>.env</c> loader that lets a browser worker read
/// <c>TRACTO_SECRET_*</c> from a file beside it instead of a hand-set shell variable.
/// </summary>
public sealed class EnvFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tracto-envfile").FullName;
    private readonly List<string> _touched = [];

    private string WriteEnv(string contents, string name = ".env")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Records a variable so it is cleared again when the test ends.</summary>
    private string Var(string name)
    {
        _touched.Add(name);
        return name;
    }

    public void Dispose()
    {
        foreach (var name in _touched)
            System.Environment.SetEnvironmentVariable(name, null);
        Directory.Delete(_dir, recursive: true);
    }

    // ── Finding the file ──────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsFalse_WhenTheFileIsMissing()
    {
        EnvFile.Load(Path.Combine(_dir, "nope.env")).Should().BeFalse();
    }

    [Fact]
    public void Load_ReturnsTrue_WhenTheFileExists()
    {
        EnvFile.Load(WriteEnv("")).Should().BeTrue();
    }

    // ── Reading values ────────────────────────────────────────────────────────

    [Fact]
    public void Load_SetsAVariableFromTheFile()
    {
        var key = Var("TRACTO_SECRET_ENVFILE_PLAIN");
        EnvFile.Load(WriteEnv($"{key}=hunter2"));

        System.Environment.GetEnvironmentVariable(key).Should().Be("hunter2");
    }

    [Theory]
    [InlineData("\"quoted value\"", "quoted value")]
    [InlineData("'quoted value'", "quoted value")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("p@ss=w0rd#1", "p@ss=w0rd#1")]   // only the first = splits; # inside a value stays
    [InlineData("\"mismatched'", "\"mismatched'")]
    public void Load_ReadsTheValueAsWritten(string written, string expected)
    {
        var key = Var("TRACTO_SECRET_ENVFILE_VALUE");
        EnvFile.Load(WriteEnv($"{key}={written}"));

        System.Environment.GetEnvironmentVariable(key).Should().Be(expected);
    }

    [Fact]
    public void Load_AcceptsAnExportPrefixAndSpacesAroundTheKey()
    {
        var key = Var("TRACTO_SECRET_ENVFILE_EXPORT");
        EnvFile.Load(WriteEnv($"export   {key} = spaced-out"));

        System.Environment.GetEnvironmentVariable(key).Should().Be("spaced-out");
    }

    [Fact]
    public void Load_IgnoresBlankLinesCommentsAndLinesWithNoKey()
    {
        var key = Var("TRACTO_SECRET_ENVFILE_MIXED");
        var commented = Var("TRACTO_SECRET_ENVFILE_COMMENTED");

        EnvFile.Load(WriteEnv($"""
            # a comment

            #{commented}=should-not-be-set
            =novalue
            not-a-pair
            {key}=kept
            """));

        System.Environment.GetEnvironmentVariable(key).Should().Be("kept");
        System.Environment.GetEnvironmentVariable(commented).Should().BeNull();
    }

    [Fact]
    public void Load_SkipsAByteOrderMarkOnTheFirstLine()
    {
        var key = Var("TRACTO_SECRET_ENVFILE_BOM");
        EnvFile.Load(WriteEnv($"﻿{key}=after-bom"));

        System.Environment.GetEnvironmentVariable(key).Should().Be("after-bom");
    }

    // ── Precedence ────────────────────────────────────────────────────────────

    [Fact]
    public void Load_LeavesARealEnvironmentVariableAlone()
    {
        var key = Var("TRACTO_SECRET_ENVFILE_PRECEDENCE");
        System.Environment.SetEnvironmentVariable(key, "from-the-shell");

        EnvFile.Load(WriteEnv($"{key}=from-the-file"));

        System.Environment.GetEnvironmentVariable(key).Should().Be("from-the-shell");
    }

    [Fact]
    public void LoadAll_ReadsEachFileThatExistsAndReportsThemInOrder()
    {
        var first  = Var("TRACTO_SECRET_ENVFILE_FIRST");
        var second = Var("TRACTO_SECRET_ENVFILE_SECOND");
        var shared = Var("TRACTO_SECRET_ENVFILE_SHARED");

        var a = WriteEnv($"{first}=a\n{shared}=from-a", "a.env");
        var b = WriteEnv($"{second}=b\n{shared}=from-b", "b.env");

        var loaded = EnvFile.LoadAll(a, Path.Combine(_dir, "missing.env"), b);

        loaded.Should().Equal(a, b);
        System.Environment.GetEnvironmentVariable(first).Should().Be("a");
        System.Environment.GetEnvironmentVariable(second).Should().Be("b");
        // The first file to set a name wins, exactly as a real variable beats the file.
        System.Environment.GetEnvironmentVariable(shared).Should().Be("from-a");
    }

    [Fact]
    public void LoadAll_ReadsAFileNamedTwiceOnlyOnce()
    {
        // The worker names the folder beside the executable and the working directory. When it
        // is started from its own output folder those are the same file, and it must be
        // reported once rather than twice.
        var key = Var("TRACTO_SECRET_ENVFILE_ONCE");
        var path = WriteEnv($"{key}=once", "same.env");

        var loaded = EnvFile.LoadAll(path, Path.Combine(_dir, ".", "same.env"));

        loaded.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(path));
        System.Environment.GetEnvironmentVariable(key).Should().Be("once");
    }
}
