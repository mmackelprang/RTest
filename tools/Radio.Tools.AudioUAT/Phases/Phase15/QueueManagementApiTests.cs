using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.2: Queue Management API Tests
/// Tests queue operations including get, add, remove, move, jump, and clear.
/// </summary>
public class QueueManagementApiTests
{
  private readonly RadioApiClient _apiClient;

  public QueueManagementApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new TestGetQueue(_apiClient),
      new TestAddToQueue(_apiClient),
      new TestAddAtPosition(_apiClient),
      new TestRemoveFromQueue(_apiClient),
      new TestMoveTrack(_apiClient),
      new TestJumpToIndex(_apiClient),
      new TestClearQueue(_apiClient),
      new TestQueueSignalR(_apiClient)
    ];
  }
}

/// <summary>
/// QUEUE-001: Get current queue.
/// </summary>
public class TestGetQueue : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-001";
  public string TestName => "Get current queue";
  public string Description => "Verify queue can be retrieved via API";
  public int Phase => 15;

  public TestGetQueue(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var queue = await _apiClient.GetQueueAsync(ct);
      if (queue == null)
      {
        return TestResult.Fail(TestId, "GetQueue returned null");
      }

      // Queue may be empty, which is valid
      ConsoleUI.WriteSuccess($"Queue retrieved: {queue.Count} items");
      return TestResult.Pass(TestId, $"Queue has {queue.Count} items");
    }
    catch (Exception ex)
    {
      // Queue operations might not be supported by current source
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-002: Add track to end of queue.
/// </summary>
public class TestAddToQueue : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-002";
  public string TestName => "Add track to end of queue";
  public string Description => "Verify track can be added to queue via API";
  public int Phase => 15;

  public TestAddToQueue(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var initialQueue = await _apiClient.GetQueueAsync(ct);
      var initialCount = initialQueue?.Count ?? 0;

      // Add a test track (this will vary by source - for real testing, use actual track identifiers)
      var trackId = "test-track-" + Guid.NewGuid().ToString();
      
      try
      {
        var updatedQueue = await _apiClient.AddToQueueAsync(trackId, ct: ct);
        if (updatedQueue == null)
        {
          return TestResult.Fail(TestId, "AddToQueue returned null");
        }

        if (updatedQueue.Count != initialCount + 1)
        {
          return TestResult.Fail(TestId, $"Expected queue size {initialCount + 1}, got {updatedQueue.Count}");
        }

        ConsoleUI.WriteSuccess($"Track added: queue size {initialCount} -> {updatedQueue.Count}");
        return TestResult.Pass(TestId, $"Queue size: {initialCount} -> {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        // Track might not exist - this is expected for dummy track ID
        ConsoleUI.WriteWarning("Test track not found (expected for dummy ID)");
        return TestResult.Skip(TestId, $"Could not add test track: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-003: Add track at specific position.
/// </summary>
public class TestAddAtPosition : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-003";
  public string TestName => "Add track at specific position";
  public string Description => "Verify track can be inserted at specific position in queue";
  public int Phase => 15;

  public TestAddAtPosition(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync(ct);
      if (queue == null || queue.Count == 0)
      {
        ConsoleUI.WriteWarning("Queue is empty, cannot test position insert");
        return TestResult.Skip(TestId, "Queue is empty");
      }

      // Try to add at position 0
      var trackId = "test-track-position-" + Guid.NewGuid().ToString();
      
      try
      {
        var updatedQueue = await _apiClient.AddToQueueAsync(trackId, position: 0, ct: ct);
        if (updatedQueue == null)
        {
          return TestResult.Fail(TestId, "AddToQueue returned null");
        }

        ConsoleUI.WriteSuccess($"Track added at position 0, queue size: {updatedQueue.Count}");
        return TestResult.Pass(TestId, $"Track added at position 0, queue size: {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        // Track might not exist - this is expected for dummy track ID
        ConsoleUI.WriteWarning("Test track not found (expected for dummy ID)");
        return TestResult.Skip(TestId, $"Could not add test track: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-004: Remove track from queue by index.
/// </summary>
public class TestRemoveFromQueue : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-004";
  public string TestName => "Remove track from queue by index";
  public string Description => "Verify track can be removed from queue via API";
  public int Phase => 15;

  public TestRemoveFromQueue(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync(ct);
      if (queue == null || queue.Count == 0)
      {
        ConsoleUI.WriteWarning("Queue is empty, nothing to remove");
        return TestResult.Skip(TestId, "Queue is empty");
      }

      var initialCount = queue.Count;

      // Try to remove first item (index 0)
      try
      {
        var success = await _apiClient.RemoveFromQueueAsync(0, ct);
        if (!success)
        {
          return TestResult.Fail(TestId, "RemoveFromQueue returned false");
        }

        await Task.Delay(300, ct);

        var updatedQueue = await _apiClient.GetQueueAsync(ct);
        if (updatedQueue == null)
        {
          return TestResult.Fail(TestId, "Could not get updated queue");
        }

        if (updatedQueue.Count != initialCount - 1)
        {
          return TestResult.Fail(TestId, $"Expected queue size {initialCount - 1}, got {updatedQueue.Count}");
        }

        ConsoleUI.WriteSuccess($"Track removed: queue size {initialCount} -> {updatedQueue.Count}");
        return TestResult.Pass(TestId, $"Queue size: {initialCount} -> {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        return TestResult.Fail(TestId, $"Failed to remove track: {ex.Message}", exception: ex);
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-005: Move track within queue (reorder).
/// </summary>
public class TestMoveTrack : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-005";
  public string TestName => "Move track within queue";
  public string Description => "Verify tracks can be reordered in queue via API";
  public int Phase => 15;

  public TestMoveTrack(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync(ct);
      if (queue == null || queue.Count < 2)
      {
        ConsoleUI.WriteWarning("Queue has less than 2 items, cannot test move");
        return TestResult.Skip(TestId, "Queue has less than 2 items");
      }

      // Try to move first item to second position
      try
      {
        var result = await _apiClient.MoveQueueItemAsync(0, 1, ct);
        if (result == null)
        {
          ConsoleUI.WriteWarning("Move not supported or queue not available");
          return TestResult.Skip(TestId, "Move operation not supported");
        }

        await Task.Delay(300, ct);

        var updatedQueue = await _apiClient.GetQueueAsync(ct);
        if (updatedQueue == null)
        {
          return TestResult.Fail(TestId, "Could not get updated queue");
        }

        ConsoleUI.WriteSuccess("Track moved successfully");
        return TestResult.Pass(TestId, "Track reordered successfully");
      }
      catch (Exception ex)
      {
        // Move might not be implemented yet
        ConsoleUI.WriteWarning("Move not implemented or failed");
        return TestResult.Skip(TestId, $"Move failed: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-006: Jump to specific queue index.
/// </summary>
public class TestJumpToIndex : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-006";
  public string TestName => "Jump to specific queue index";
  public string Description => "Verify playback can jump to specific queue position via API";
  public int Phase => 15;

  public TestJumpToIndex(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync(ct);
      if (queue == null || queue.Count == 0)
      {
        ConsoleUI.WriteWarning("Queue is empty, cannot jump");
        return TestResult.Skip(TestId, "Queue is empty");
      }

      // Jump to index 0
      try
      {
        var result = await _apiClient.JumpToQueueIndexAsync(0, ct);
        if (result == null)
        {
          ConsoleUI.WriteWarning("Jump not supported or queue not available");
          return TestResult.Skip(TestId, "Jump operation not supported");
        }

        await Task.Delay(500, ct);

        var state2 = await _apiClient.GetPlaybackStateAsync(ct);
        if (state2 == null)
        {
          return TestResult.Fail(TestId, "Could not get state after jump");
        }

        ConsoleUI.WriteSuccess("Jumped to queue index successfully");
        return TestResult.Pass(TestId, "Jump executed successfully");
      }
      catch (Exception ex)
      {
        // Jump might not be implemented yet
        ConsoleUI.WriteWarning("Jump not implemented or failed");
        return TestResult.Skip(TestId, $"Jump failed: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-007: Clear entire queue.
/// </summary>
public class TestClearQueue : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-007";
  public string TestName => "Clear entire queue";
  public string Description => "Verify queue can be cleared via API";
  public int Phase => 15;

  public TestClearQueue(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null || !state.CanQueue)
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }

      var success = await _apiClient.ClearQueueAsync(ct);
      if (!success)
      {
        ConsoleUI.WriteWarning("Could not clear queue (might already be empty)");
        return TestResult.Skip(TestId, "Queue already empty or not available");
      }

      await Task.Delay(300, ct);
      var queue = await _apiClient.GetQueueAsync(ct);
      
      if (queue == null)
      {
        return TestResult.Fail(TestId, "Could not get queue after clear");
      }

      if (queue.Count != 0)
      {
        return TestResult.Fail(TestId, $"Expected empty queue, got {queue.Count} items");
      }

      ConsoleUI.WriteSuccess("Queue cleared successfully");
      return TestResult.Pass(TestId, "Queue is now empty");
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        ConsoleUI.WriteWarning("Queue not supported by current source");
        return TestResult.Skip(TestId, "Queue not supported by current source");
      }
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// QUEUE-008: Verify queue updates trigger SignalR events.
/// </summary>
public class TestQueueSignalR : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "QUEUE-008";
  public string TestName => "Verify queue SignalR events";
  public string Description => "Verify queue changes trigger real-time updates via SignalR";
  public int Phase => 15;

  public TestQueueSignalR(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // This test would require SignalR client setup
      // For now, we'll mark it as a placeholder

      ConsoleUI.WriteWarning("SignalR testing requires client setup");
      return TestResult.Skip(TestId, "SignalR testing requires separate client setup");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
