using System.Text.Json;

namespace Radio.Tools.AudioUAT.Results;

/// <summary>
/// Manages test results storage and reporting.
/// </summary>
public class TestResultsManager
{
  private readonly List<TestResult> _results = [];
  private readonly object _lock = new();

  /// <summary>
  /// Adds a test result.
  /// </summary>
  /// <param name="result">The test result to add.</param>
  public void AddResult(TestResult result)
  {
    lock (_lock)
    {
      // Remove any existing result for this test ID
      _results.RemoveAll(r => r.TestId == result.TestId);
      _results.Add(result);
    }
  }

  /// <summary>
  /// Gets all test results.
  /// </summary>
  /// <returns>A read-only list of all results.</returns>
  public IReadOnlyList<TestResult> GetAllResults()
  {
    lock (_lock)
    {
      return _results.ToList().AsReadOnly();
    }
  }

  /// <summary>
  /// Gets test results for a specific phase.
  /// </summary>
  /// <param name="phase">The phase number.</param>
  /// <returns>A read-only list of results for the phase.</returns>
  public IReadOnlyList<TestResult> GetResultsForPhase(int phase)
  {
    lock (_lock)
    {
      return _results
        .Where(r => r.TestId.StartsWith($"P{phase}-"))
        .ToList()
        .AsReadOnly();
    }
  }

  /// <summary>
  /// Gets a summary of all test results.
  /// </summary>
  /// <returns>The test summary.</returns>
  public TestSummary GetSummary()
  {
    lock (_lock)
    {
      return new TestSummary
      {
        TotalTests = _results.Count,
        PassedTests = _results.Count(r => r.Passed),
        FailedTests = _results.Count(r => !r.Passed && !r.Skipped),
        SkippedTests = _results.Count(r => r.Skipped),
        TotalDuration = TimeSpan.FromTicks(_results.Sum(r => r.Duration.Ticks))
      };
    }
  }

  /// <summary>
  /// Gets a summary of test results for a specific phase.
  /// </summary>
  /// <param name="phase">The phase number.</param>
  /// <returns>The test summary for the phase.</returns>
  public TestSummary GetSummaryForPhase(int phase)
  {
    lock (_lock)
    {
      var phaseResults = _results.Where(r => r.TestId.StartsWith($"P{phase}-")).ToList();
      return new TestSummary
      {
        TotalTests = phaseResults.Count,
        PassedTests = phaseResults.Count(r => r.Passed),
        FailedTests = phaseResults.Count(r => !r.Passed && !r.Skipped),
        SkippedTests = phaseResults.Count(r => r.Skipped),
        TotalDuration = TimeSpan.FromTicks(phaseResults.Sum(r => r.Duration.Ticks))
      };
    }
  }

  /// <summary>
  /// Clears all test results.
  /// </summary>
  public void Clear()
  {
    lock (_lock)
    {
      _results.Clear();
    }
  }

  /// <summary>
  /// Exports test results to a JSON file.
  /// </summary>
  /// <param name="filePath">The output file path.</param>
  public async Task ExportToJsonAsync(string filePath)
  {
    IReadOnlyList<TestResult> results;
    lock (_lock)
    {
      results = _results.ToList().AsReadOnly();
    }

    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    var json = JsonSerializer.Serialize(new
    {
      exportedAt = DateTime.UtcNow,
      summary = GetSummary(),
      results = results.Select(r => new
      {
        testId = r.TestId,
        passed = r.Passed,
        skipped = r.Skipped,
        message = r.Message,
        durationMs = r.Duration.TotalMilliseconds,
        exception = r.Exception?.Message,
        metadata = r.Metadata
      })
    }, options);

    await File.WriteAllTextAsync(filePath, json);
  }
}

/// <summary>
/// Summary of test results.
/// </summary>
public class TestSummary
{
  /// <summary>
  /// Gets or sets the total number of tests.
  /// </summary>
  public int TotalTests { get; set; }

  /// <summary>
  /// Gets or sets the number of passed tests.
  /// </summary>
  public int PassedTests { get; set; }

  /// <summary>
  /// Gets or sets the number of failed tests.
  /// </summary>
  public int FailedTests { get; set; }

  /// <summary>
  /// Gets or sets the number of skipped tests.
  /// </summary>
  public int SkippedTests { get; set; }

  /// <summary>
  /// Gets or sets the total duration of all tests.
  /// </summary>
  public TimeSpan TotalDuration { get; set; }

  /// <summary>
  /// Gets the pass rate as a percentage.
  /// </summary>
  public double PassRate => TotalTests > 0 ? (double)PassedTests / TotalTests * 100 : 0;
}
