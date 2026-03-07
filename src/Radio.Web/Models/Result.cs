namespace Radio.Web.Models;

/// <summary>
/// Represents the outcome of an API call with typed data or an error.
/// Use instead of throwing exceptions from API service methods.
/// </summary>
/// <typeparam name="T">The type of the successful result value.</typeparam>
public class Result<T>
{
  public T? Value { get; }
  public string? Error { get; }
  public bool IsSuccess { get; }
  public bool IsDisconnected { get; }

  private Result(T? value, string? error, bool isSuccess, bool isDisconnected)
  {
    Value = value;
    Error = error;
    IsSuccess = isSuccess;
    IsDisconnected = isDisconnected;
  }

  /// <summary>Creates a successful result with a value.</summary>
  public static Result<T> Success(T value) => new(value, null, true, false);

  /// <summary>Creates an error result with a message.</summary>
  public static Result<T> Fail(string error) => new(default, error, false, false);

  /// <summary>Creates a disconnected result (API unreachable).</summary>
  public static Result<T> Disconnected() => new(default, "API is not available", false, true);

  /// <summary>Maps the value to a new type if successful.</summary>
  public Result<TOut> Map<TOut>(Func<T, TOut> mapper)
  {
    if (!IsSuccess || Value == null)
    {
      return IsDisconnected
        ? Result<TOut>.Disconnected()
        : Result<TOut>.Fail(Error ?? "Unknown error");
    }

    return Result<TOut>.Success(mapper(Value));
  }
}

/// <summary>
/// Represents the outcome of an API call with no return value.
/// </summary>
public class Result
{
  public string? Error { get; }
  public bool IsSuccess { get; }
  public bool IsDisconnected { get; }

  private Result(string? error, bool isSuccess, bool isDisconnected)
  {
    Error = error;
    IsSuccess = isSuccess;
    IsDisconnected = isDisconnected;
  }

  /// <summary>Creates a successful result.</summary>
  public static Result Success() => new(null, true, false);

  /// <summary>Creates an error result with a message.</summary>
  public static Result Fail(string error) => new(error, false, false);

  /// <summary>Creates a disconnected result (API unreachable).</summary>
  public static Result Disconnected() => new("API is not available", false, true);
}
