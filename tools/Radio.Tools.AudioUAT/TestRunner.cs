using Radio.Tools.AudioUAT.TestResults;

namespace Radio.Tools.AudioUAT;

/// <summary>
/// Interface for phase test implementations.
/// </summary>
public interface IPhaseTest
{
  /// <summary>
  /// Gets the unique test identifier (e.g., "P2-001").
  /// </summary>
  string TestId { get; }

  /// <summary>
  /// Gets the human-readable test name.
  /// </summary>
  string TestName { get; }

  /// <summary>
  /// Gets the test description.
  /// </summary>
  string Description { get; }

  /// <summary>
  /// Gets the phase number this test belongs to.
  /// </summary>
  int Phase { get; }

  /// <summary>
  /// Executes the test.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The test result.</returns>
  Task<TestResult> ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of executing a test.
/// </summary>
public record TestResult
{
  /// <summary>
  /// Gets the test identifier.
  /// </summary>
  public required string TestId { get; init; }

  /// <summary>
  /// Gets whether the test passed.
  /// </summary>
  public required bool Passed { get; init; }

  /// <summary>
  /// Gets an optional message describing the result.
  /// </summary>
  public string? Message { get; init; }

  /// <summary>
  /// Gets the test execution duration.
  /// </summary>
  public TimeSpan Duration { get; init; }

  /// <summary>
  /// Gets any exception that occurred during the test.
  /// </summary>
  public Exception? Exception { get; init; }

  /// <summary>
  /// Gets optional metadata about the test execution.
  /// </summary>
  public Dictionary<string, object>? Metadata { get; init; }

  /// <summary>
  /// Gets whether this test was skipped.
  /// </summary>
  public bool Skipped { get; init; }

  /// <summary>
  /// Creates a passed test result.
  /// </summary>
  public static TestResult Pass(string testId, string? message = null, TimeSpan duration = default,
    Dictionary<string, object>? metadata = null) =>
    new()
    {
      TestId = testId,
      Passed = true,
      Message = message,
      Duration = duration,
      Metadata = metadata
    };

  /// <summary>
  /// Creates a failed test result.
  /// </summary>
  public static TestResult Fail(string testId, string? message = null, TimeSpan duration = default,
    Exception? exception = null, Dictionary<string, object>? metadata = null) =>
    new()
    {
      TestId = testId,
      Passed = false,
      Message = message,
      Duration = duration,
      Exception = exception,
      Metadata = metadata
    };

  /// <summary>
  /// Creates a skipped test result.
  /// </summary>
  public static TestResult Skip(string testId, string? message = null) =>
    new()
    {
      TestId = testId,
      Passed = false,
      Skipped = true,
      Message = message
    };
}

/// <summary>
/// Manages test execution for phase tests.
/// </summary>
public class TestRunner
{
  private readonly TestResultsManager _resultsManager;

  /// <summary>
  /// Initializes a new instance of the <see cref="TestRunner"/> class.
  /// </summary>
  /// <param name="resultsManager">The results manager.</param>
  public TestRunner(TestResultsManager resultsManager)
  {
    _resultsManager = resultsManager;
  }

  /// <summary>
  /// Executes a single test with timing and exception handling.
  /// </summary>
  /// <param name="test">The test to execute.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The test result.</returns>
  public async Task<TestResult> RunTestAsync(IPhaseTest test, CancellationToken ct = default)
  {
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    try
    {
      var result = await test.ExecuteAsync(ct);
      stopwatch.Stop();

      // Update duration if not set
      if (result.Duration == default)
      {
        result = result with { Duration = stopwatch.Elapsed };
      }

      _resultsManager.AddResult(result);
      return result;
    }
    catch (OperationCanceledException)
    {
      stopwatch.Stop();
      var result = TestResult.Skip(test.TestId, "Test was cancelled");
      result = result with { Duration = stopwatch.Elapsed };
      _resultsManager.AddResult(result);
      return result;
    }
    catch (Exception ex)
    {
      stopwatch.Stop();
      var result = TestResult.Fail(
        test.TestId,
        $"Test failed with exception: {ex.Message}",
        stopwatch.Elapsed,
        ex);
      _resultsManager.AddResult(result);
      return result;
    }
  }

  /// <summary>
  /// Executes all tests in a collection.
  /// </summary>
  /// <param name="tests">The tests to execute.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>All test results.</returns>
  public async Task<IReadOnlyList<TestResult>> RunAllTestsAsync(
    IEnumerable<IPhaseTest> tests,
    CancellationToken ct = default)
  {
    var results = new List<TestResult>();

    foreach (var test in tests)
    {
      if (ct.IsCancellationRequested)
        break;

      var result = await RunTestAsync(test, ct);
      results.Add(result);
    }

    return results.AsReadOnly();
  }
}
