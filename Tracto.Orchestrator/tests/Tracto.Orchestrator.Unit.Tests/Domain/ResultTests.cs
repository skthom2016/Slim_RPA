using FluentAssertions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Unit.Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="Result{T,TError}"/> discriminated union.
/// Verifies the monad contract that all layers depend on for business-error propagation.
/// </summary>
public sealed class ResultTests
{
    // ─── Ok ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ok_SetsIsSuccessTrue()
    {
        var result = Result<int, Error>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Ok_ValueIsAccessible()
    {
        var result = Result<string, Error>.Ok("hello");

        result.Value.Should().Be("hello");
    }

    [Fact]
    public void Ok_AccessingError_Throws()
    {
        var result = Result<int, Error>.Ok(1);

        var act = () => _ = result.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Fail ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_SetsIsFailureTrue()
    {
        var result = Result<int, Error>.Fail(Error.Validation("bad"));

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Fail_ErrorIsAccessible()
    {
        var error  = Error.Validation("must not be empty");
        var result = Result<string, Error>.Fail(error);

        result.Error.Should().Be(error);
    }

    [Fact]
    public void Fail_AccessingValue_Throws()
    {
        var result = Result<int, Error>.Fail(Error.NotFound("item not found"));

        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Match ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_OnSuccess_InvokesOnSuccessBranch()
    {
        var result = Result<int, Error>.Ok(7);

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"err:{e.Code}");

        output.Should().Be("ok:7");
    }

    [Fact]
    public void Match_OnFailure_InvokesOnFailureBranch()
    {
        var result = Result<int, Error>.Fail(Error.Conflict("duplicate"));

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"err:{e.Code}");

        output.Should().StartWith("err:");
    }

    // ─── Map ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int, Error>.Ok(5).Map(v => v * 2);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Map_OnFailure_PropagatesError()
    {
        var error  = Error.Validation("invalid");
        var result = Result<int, Error>.Fail(error).Map(v => v * 2);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    // ─── Bind ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_OnSuccess_ChainsNextOperation()
    {
        var result = Result<int, Error>.Ok(3)
                                       .Bind(v => Result<string, Error>.Ok($"value:{v}"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("value:3");
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuits()
    {
        var error  = Error.NotFound("missing");
        var result = Result<int, Error>.Fail(error)
                                       .Bind(v => Result<string, Error>.Ok("unreachable"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    // ─── Implicit conversions ──────────────────────────────────────────────────

    [Fact]
    public void ImplicitConversion_FromValue_CreatesOk()
    {
        Result<int, Error> result = 99;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(99);
    }

    [Fact]
    public void ImplicitConversion_FromError_CreatesFail()
    {
        Result<int, Error> result = Error.General("CODE", "message");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CODE");
    }
}
