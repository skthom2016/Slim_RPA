#nullable enable
namespace Tracto.Orchestrator.Domain.Common;

/// <summary>
/// Discriminated union that holds either a success value or a domain Error.
/// Business errors must use this type; never throw for expected failure paths.
/// </summary>
public sealed class Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = default;
    }

    private Result(TError error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public TError Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException("Cannot access Error on a successful Result.");

    // ── Constructors / canonical factory ─────────────────────────────────────

    /// <summary>Creates a successful result. Canonical alias: <c>Ok</c>.</summary>
    public static Result<TValue, TError> Success(TValue value) => new(value);

    /// <summary>Creates a failed result. Canonical alias: <c>Fail</c>.</summary>
    public static Result<TValue, TError> Failure(TError error) => new(error);

    /// <summary>Short alias for <see cref="Success"/>.</summary>
    public static Result<TValue, TError> Ok(TValue value) => new(value);

    /// <summary>Short alias for <see cref="Failure"/>.</summary>
    public static Result<TValue, TError> Fail(TError error) => new(error);

    // ── Implicit conversions ──────────────────────────────────────────────────

    /// <summary>Wraps a value in a successful result automatically.</summary>
    public static implicit operator Result<TValue, TError>(TValue value) => new(value);

    /// <summary>Wraps an error in a failed result automatically.</summary>
    public static implicit operator Result<TValue, TError>(TError error) => new(error);

    // ── Monad operations ──────────────────────────────────────────────────────

    /// <summary>Pattern-matches the two branches and returns the projected value.</summary>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<TError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    /// <summary>
    /// Transforms the success value. On failure the error propagates unchanged.
    /// </summary>
    public Result<TOut, TError> Map<TOut>(Func<TValue, TOut> selector) =>
        IsSuccess
            ? Result<TOut, TError>.Ok(selector(_value!))
            : Result<TOut, TError>.Fail(_error!);

    /// <summary>
    /// Chains a next operation that itself returns a Result (monadic bind / flatMap).
    /// On failure the original error short-circuits and <paramref name="binder"/> is never called.
    /// </summary>
    public Result<TOut, TError> Bind<TOut>(Func<TValue, Result<TOut, TError>> binder) =>
        IsSuccess
            ? binder(_value!)
            : Result<TOut, TError>.Fail(_error!);
}
