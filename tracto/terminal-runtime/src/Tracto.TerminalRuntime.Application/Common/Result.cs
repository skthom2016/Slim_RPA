namespace Tracto.TerminalRuntime.Application.Common;

/// <summary>
/// Discriminated union: either a success value of type T or an Error.
/// Avoids exception-based control flow for expected business failures.
/// </summary>
public sealed class Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    private Result(TError error)
    {
        IsSuccess = false;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public TError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful Result.");

    public static Result<T, TError> Ok(T value) => new(value);
    public static Result<T, TError> Fail(TError error) => new(error);

    /// <summary>Alias for <see cref="Ok"/> — used by Infrastructure journal code.</summary>
    public static Result<T, TError> Success(T value) => new(value);

    /// <summary>Implicit conversion from a value so callers can write <c>return value;</c>.</summary>
    public static implicit operator Result<T, TError>(T value) => Ok(value);

    /// <summary>Implicit conversion from an error so callers can write <c>return error;</c>.</summary>
    public static implicit operator Result<T, TError>(TError error) => Fail(error);

    public Result<TNext, TError> Map<TNext>(Func<T, TNext> map) =>
        IsSuccess ? Result<TNext, TError>.Ok(map(_value!)) : Result<TNext, TError>.Fail(_error!);

    public Result<TNext, TError> Bind<TNext>(Func<T, Result<TNext, TError>> bind) =>
        IsSuccess ? bind(_value!) : Result<TNext, TError>.Fail(_error!);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<TError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);
}

/// <summary>Convenience alias pinned to the project's Error type.</summary>
public static class Result
{
    public static Result<T, Error> Ok<T>(T value) => Result<T, Error>.Ok(value);
    public static Result<T, Error> Fail<T>(Error error) => Result<T, Error>.Fail(error);
}
