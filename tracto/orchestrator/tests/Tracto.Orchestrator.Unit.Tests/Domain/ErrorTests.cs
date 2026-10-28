using FluentAssertions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Unit.Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="Error"/> value type.
/// </summary>
public sealed class ErrorTests
{
    [Fact]
    public void None_HasEmptyCodeAndMessage()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Message.Should().BeEmpty();
    }

    [Fact]
    public void Validation_SetsCodeAndMessage()
    {
        var err = Error.Validation("field is required");

        err.Code.Should().NotBeNullOrWhiteSpace();
        err.Message.Should().Be("field is required");
    }

    [Fact]
    public void NotFound_SetsCodeAndMessage()
    {
        var err = Error.NotFound("run not found");

        err.Code.Should().NotBeNullOrWhiteSpace();
        err.Message.Should().Be("run not found");
    }

    [Fact]
    public void Conflict_SetsCodeAndMessage()
    {
        var err = Error.Conflict("stale attemptId");

        err.Code.Should().NotBeNullOrWhiteSpace();
        err.Message.Should().Be("stale attemptId");
    }

    [Fact]
    public void General_SetsExplicitCodeAndMessage()
    {
        var err = Error.General("MY_CODE", "my message");

        err.Code.Should().Be("MY_CODE");
        err.Message.Should().Be("my message");
    }

    [Fact]
    public void RecordEquality_SameCodeAndMessage_AreEqual()
    {
        var a = Error.Validation("same");
        var b = Error.Validation("same");

        a.Should().Be(b, because: "Error is a record with value-equality semantics");
    }

    [Fact]
    public void RecordEquality_DifferentMessage_AreNotEqual()
    {
        var a = Error.Validation("first");
        var b = Error.Validation("second");

        a.Should().NotBe(b);
    }

    [Fact]
    public void IsNone_ForNoneError_IsTrue()
    {
        Error.None.IsNone.Should().BeTrue();
    }

    [Fact]
    public void IsNone_ForValidationError_IsFalse()
    {
        Error.Validation("x").IsNone.Should().BeFalse();
    }
}
