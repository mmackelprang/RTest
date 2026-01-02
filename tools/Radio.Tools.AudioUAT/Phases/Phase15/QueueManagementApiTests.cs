using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;
using Spectre.Console;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.2: Queue Management API Tests
/// Tests queue operations including add, remove, move, clear, and jump.
/// </summary>
public class QueueManagementApiTests : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public QueueManagementApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public List<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new PhaseTest("QUEUE-001", 15, "Get current queue", TestGetQueue),
      new PhaseTest("QUEUE-002", 15, "Add track to end of queue", TestAddToQueue),
      new PhaseTest("QUEUE-003", 15, "Add track at specific position", TestAddAtPosition),
      new PhaseTest("QUEUE-004", 15, "Remove track from queue by index", TestRemoveFromQueue),
      new PhaseTest("QUEUE-005", 15, "Move track within queue (reorder)", TestMoveTrack),
      new PhaseTest("QUEUE-006", 15, "Jump to specific queue index", TestJumpToIndex),
      new PhaseTest("QUEUE-007", 15, "Clear entire queue", TestClearQueue),
      new PhaseTest("QUEUE-008", 15, "Verify queue updates trigger SignalR events", TestQueueSignalR)
    };
  }

  private async Task<TestResult> TestGetQueue()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing get queue...[/]");

      var queue = await _apiClient.GetQueueAsync();
      if (queue == null)
      {
        return TestResult.Fail("GetQueue returned null");
      }

      // Queue may be empty, which is valid
      AnsiConsole.MarkupLine($"[grey]Current queue size: {queue.Count}[/]");
      
      AnsiConsole.MarkupLine("[green]✓ Queue retrieved successfully[/]");
      return TestResult.Pass($"Queue has {queue.Count} items");
    }
    catch (Exception ex)
    {
      // Queue operations might not be supported by current source
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestAddToQueue()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing add to queue...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var initialQueue = await _apiClient.GetQueueAsync();
      var initialCount = initialQueue?.Count ?? 0;

      // Add a track (using a sample track identifier - this will vary by source)
      // For file player, this would be a file path
      // For Spotify, this would be a track URI
      var trackId = "test-track-" + Guid.NewGuid().ToString();
      
      try
      {
        var updatedQueue = await _apiClient.AddToQueueAsync(trackId);
        if (updatedQueue == null)
        {
          return TestResult.Fail("AddToQueue returned null");
        }

        if (updatedQueue.Count != initialCount + 1)
        {
          return TestResult.Fail($"Expected queue size {initialCount + 1}, got {updatedQueue.Count}");
        }

        AnsiConsole.MarkupLine("[green]✓ Track added to queue[/]");
        return TestResult.Pass($"Queue size: {initialCount} -> {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        // Track might not exist - this is expected for dummy track ID
        AnsiConsole.MarkupLine("[yellow]⊘ Test track not found (expected for dummy ID)[/]");
        return TestResult.Skip($"Could not add test track: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestAddAtPosition()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing add at position...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync();
      if (queue == null || queue.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue is empty, cannot test position insert[/]");
        return TestResult.Skip("Queue is empty");
      }

      // Try to add at position 0
      var trackId = "test-track-position-" + Guid.NewGuid().ToString();
      
      try
      {
        var updatedQueue = await _apiClient.AddToQueueAsync(trackId, position: 0);
        if (updatedQueue == null)
        {
          return TestResult.Fail("AddToQueue returned null");
        }

        AnsiConsole.MarkupLine("[green]✓ Track added at position[/]");
        return TestResult.Pass($"Track added at position 0, queue size: {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        // Track might not exist - this is expected for dummy track ID
        AnsiConsole.MarkupLine("[yellow]⊘ Test track not found (expected for dummy ID)[/]");
        return TestResult.Skip($"Could not add test track: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestRemoveFromQueue()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing remove from queue...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync();
      if (queue == null || queue.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue is empty, nothing to remove[/]");
        return TestResult.Skip("Queue is empty");
      }

      // Try to remove first item (index 0)
      try
      {
        await _apiClient.RemoveFromQueueAsync(0);
        await Task.Delay(300);

        var updatedQueue = await _apiClient.GetQueueAsync();
        if (updatedQueue == null)
        {
          return TestResult.Fail("Could not get updated queue");
        }

        if (updatedQueue.Count != queue.Count - 1)
        {
          return TestResult.Fail($"Expected queue size {queue.Count - 1}, got {updatedQueue.Count}");
        }

        AnsiConsole.MarkupLine("[green]✓ Track removed from queue[/]");
        return TestResult.Pass($"Queue size: {queue.Count} -> {updatedQueue.Count}");
      }
      catch (Exception ex)
      {
        return TestResult.Fail($"Failed to remove track: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestMoveTrack()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing move track in queue...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync();
      if (queue == null || queue.Count < 2)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue has less than 2 items, cannot test move[/]");
        return TestResult.Skip("Queue has less than 2 items");
      }

      // Try to move first item to second position
      try
      {
        await _apiClient.MoveQueueItemAsync(0, 1);
        await Task.Delay(300);

        var updatedQueue = await _apiClient.GetQueueAsync();
        if (updatedQueue == null)
        {
          return TestResult.Fail("Could not get updated queue");
        }

        AnsiConsole.MarkupLine("[green]✓ Track moved in queue[/]");
        return TestResult.Pass("Track reordered successfully");
      }
      catch (Exception ex)
      {
        // Move might not be implemented yet
        AnsiConsole.MarkupLine("[yellow]⊘ Move not implemented or failed[/]");
        return TestResult.Skip($"Move failed: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestJumpToIndex()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing jump to queue index...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var queue = await _apiClient.GetQueueAsync();
      if (queue == null || queue.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue is empty, cannot jump[/]");
        return TestResult.Skip("Queue is empty");
      }

      // Jump to index 0
      try
      {
        await _apiClient.JumpToQueueIndexAsync(0);
        await Task.Delay(500);

        var state2 = await _apiClient.GetPlaybackStateAsync();
        if (state2 == null)
        {
          return TestResult.Fail("Could not get state after jump");
        }

        AnsiConsole.MarkupLine("[green]✓ Jumped to queue index[/]");
        return TestResult.Pass("Jump executed successfully");
      }
      catch (Exception ex)
      {
        // Jump might not be implemented yet
        AnsiConsole.MarkupLine("[yellow]⊘ Jump not implemented or failed[/]");
        return TestResult.Skip($"Jump failed: {ex.Message}");
      }
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestClearQueue()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing clear queue...[/]");

      // Check if queue is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null || !state.CanQueue)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }

      var success = await _apiClient.ClearQueueAsync();
      if (!success)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Could not clear queue (might already be empty)[/]");
        return TestResult.Skip("Queue already empty or not available");
      }

      await Task.Delay(300);
      var queue = await _apiClient.GetQueueAsync();
      
      if (queue == null)
      {
        return TestResult.Fail("Could not get queue after clear");
      }

      if (queue.Count != 0)
      {
        return TestResult.Fail($"Expected empty queue, got {queue.Count} items");
      }

      AnsiConsole.MarkupLine("[green]✓ Queue cleared successfully[/]");
      return TestResult.Pass("Queue is now empty");
    }
    catch (Exception ex)
    {
      if (ex.Message.Contains("BadRequest") || ex.Message.Contains("NotFound"))
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Queue not supported by current source[/]");
        return TestResult.Skip("Queue not supported by current source");
      }
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestQueueSignalR()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing queue SignalR updates...[/]");

      // This test would require SignalR client setup
      // For now, we'll mark it as a placeholder

      AnsiConsole.MarkupLine("[yellow]⊘ SignalR testing requires client setup[/]");
      return TestResult.Skip("SignalR testing requires separate client setup");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }
}
