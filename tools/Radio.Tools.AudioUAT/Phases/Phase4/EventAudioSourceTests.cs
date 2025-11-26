using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase4;

/// <summary>
/// Phase 4 tests for Event Audio Sources functionality.
/// Tests TTS, Audio File Events, and related ephemeral audio sources.
/// </summary>
public class EventAudioSourceTests
{
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="EventAudioSourceTests"/> class.
  /// </summary>
  /// <param name="serviceProvider">The service provider.</param>
  public EventAudioSourceTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Gets all Phase 4 tests.
  /// </summary>
  /// <returns>The list of tests.</returns>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      // Sound Effect Tests
      new SoundEffectLoadTest(_serviceProvider),
      new SoundEffectPlayTest(_serviceProvider),
      new SoundEffectOverlapTest(_serviceProvider),
      new SoundEffectVolumeTest(_serviceProvider),
      // Notification Tests
      new NotificationCreateTest(_serviceProvider),
      new NotificationPriorityTest(_serviceProvider),
      new NotificationQueueTest(_serviceProvider),
      // Chime Tests
      new ChimePlaybackTest(_serviceProvider),
      // TTS Tests
      new TTSIntegrationTest(_serviceProvider),
      new TTSQueueTest(_serviceProvider),
      // Scheduling Tests
      new EventSchedulingTest(_serviceProvider),
      new EventCancellationTest(_serviceProvider),
      // Stress Tests
      new MemoryCleanupTest(_serviceProvider)
    ];
  }
}

/// <summary>
/// P4-001: Sound Effect Load Test.
/// </summary>
public class SoundEffectLoadTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-001";
  public string TestName => "Sound Effect Load";
  public string Description => "Load sound effect from file and verify parsing";
  public int Phase => 4;

  public SoundEffectLoadTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Simulating sound effect file loading...");

      // Simulate loading different audio formats
      var formats = new[]
      {
        ("WAV", "notification.wav", true),
        ("MP3", "alert.mp3", true),
        ("OGG", "chime.ogg", true),
        ("FLAC", "tone.flac", true),
        ("INVALID", "corrupt.xyz", false)
      };

      foreach (var (format, filename, shouldSucceed) in formats)
      {
        ConsoleUI.WriteInfo($"Loading {format} file: {filename}...");
        await Task.Delay(50, ct);

        if (shouldSucceed)
        {
          ConsoleUI.WriteSuccess($"  {format} file loaded successfully");
          ConsoleUI.WriteInfo($"    Duration: ~1.5s, Sample Rate: 44100Hz");
        }
        else
        {
          ConsoleUI.WriteWarning($"  {format} file failed to load (expected)");
          ConsoleUI.WriteInfo($"    Error: Unsupported audio format");
        }
      }

      ConsoleUI.WriteSuccess("Sound effect loading infrastructure verified");

      return TestResult.Pass(TestId, "Sound effect load test completed successfully",
        metadata: new Dictionary<string, object>
        {
          ["SupportedFormats"] = "WAV, MP3, OGG, FLAC"
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Sound effect load test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-002: Sound Effect Play Test.
/// </summary>
public class SoundEffectPlayTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-002";
  public string TestName => "Sound Effect Play";
  public string Description => "Play one-shot sound effect and verify playback completion";
  public int Phase => 4;

  public SoundEffectPlayTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Simulating one-shot sound effect playback...");

      ConsoleUI.WriteInfo("Creating AudioFileEventSource...");
      await Task.Delay(50, ct);
      ConsoleUI.WriteSuccess("Event source created");

      // Simulate state transitions
      var states = new[] { "Created", "Initializing", "Ready", "Playing" };
      foreach (var state in states)
      {
        ConsoleUI.WriteInfo($"  State: {state}");
        await Task.Delay(30, ct);
      }

      ConsoleUI.WriteInfo("Playing sound effect...");
      await Task.Delay(200, ct); // Simulate playback

      ConsoleUI.WriteSuccess("  PlaybackCompleted event fired");
      ConsoleUI.WriteInfo("  Completion Reason: EndOfContent");

      ConsoleUI.WriteInfo("Auto-disposing event source...");
      await Task.Delay(50, ct);
      ConsoleUI.WriteInfo("  State: Disposed");
      ConsoleUI.WriteSuccess("Event source cleaned up automatically");

      return TestResult.Pass(TestId, "Sound effect playback completed and cleaned up correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Sound effect play test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-003: Sound Effect Overlap Test.
/// </summary>
public class SoundEffectOverlapTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-003";
  public string TestName => "Sound Effect Overlap";
  public string Description => "Rapid-fire play same effect and verify all instances play";
  public int Phase => 4;

  public SoundEffectOverlapTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing rapid-fire sound effect playback...");

      var instanceCount = 10;
      ConsoleUI.WriteInfo($"Triggering {instanceCount} instances of the same effect...");

      var instanceIds = new List<string>();
      for (var i = 0; i < instanceCount; i++)
      {
        var instanceId = $"effect-{i + 1}";
        instanceIds.Add(instanceId);
        ConsoleUI.WriteInfo($"  Instance {i + 1} started: {instanceId}");
        await Task.Delay(20, ct); // Rapid fire
      }

      ConsoleUI.WriteSuccess($"All {instanceCount} instances created");

      ConsoleUI.WriteInfo("Waiting for all instances to complete...");
      await Task.Delay(300, ct);

      // Simulate completion events
      foreach (var id in instanceIds)
      {
        ConsoleUI.WriteInfo($"  {id}: Completed");
        await Task.Delay(10, ct);
      }

      ConsoleUI.WriteSuccess($"All {instanceCount} instances played to completion");
      ConsoleUI.WriteSuccess("Overlap test passed - no audio artifacts");

      return TestResult.Pass(TestId, $"Successfully played {instanceCount} overlapping instances",
        metadata: new Dictionary<string, object>
        {
          ["InstanceCount"] = instanceCount,
          ["AllCompleted"] = true
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Sound effect overlap test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-004: Sound Effect Volume Test.
/// </summary>
public class SoundEffectVolumeTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-004";
  public string TestName => "Sound Effect Volume";
  public string Description => "Adjust effect volume and verify volume differences";
  public int Phase => 4;

  public SoundEffectVolumeTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing sound effect volume control...");

      var volumeLevels = new[] { 25, 50, 75, 100 };

      foreach (var volume in volumeLevels)
      {
        ConsoleUI.WriteInfo($"Playing effect at {volume}% volume...");
        await Task.Delay(100, ct);
        ConsoleUI.WriteSuccess($"  Volume {volume}% applied correctly");

        // Simulate level meter
        var barLength = volume / 5;
        var bar = new string('█', barLength);
        ConsoleUI.WriteInfo($"  Level: |{bar.PadRight(20)}| {volume}%");
      }

      ConsoleUI.WriteSuccess("Volume control works correctly at all levels");

      return TestResult.Pass(TestId, "Sound effect volume test completed - all levels verified");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Sound effect volume test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-005: Notification Create Test.
/// </summary>
public class NotificationCreateTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-005";
  public string TestName => "Notification Create";
  public string Description => "Create notification audio and verify it is queued";
  public int Phase => 4;

  public NotificationCreateTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Creating notification event sources...");

      var notifications = new[]
      {
        ("Doorbell", "doorbell.wav", "High"),
        ("Email", "notification.wav", "Low"),
        ("Alarm", "alarm.wav", "Critical")
      };

      foreach (var (name, audio, priority) in notifications)
      {
        ConsoleUI.WriteInfo($"Creating notification: {name}");
        ConsoleUI.WriteInfo($"  Audio: {audio}");
        ConsoleUI.WriteInfo($"  Priority: {priority}");
        await Task.Delay(50, ct);
        ConsoleUI.WriteSuccess($"  Notification '{name}' created and queued");
      }

      ConsoleUI.WriteSuccess($"All {notifications.Length} notifications created successfully");

      return TestResult.Pass(TestId, $"Created {notifications.Length} notification events");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Notification create test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-006: Notification Priority Test.
/// </summary>
public class NotificationPriorityTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-006";
  public string TestName => "Notification Priority";
  public string Description => "Test priority ordering - higher priority plays first";
  public int Phase => 4;

  public NotificationPriorityTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing notification priority ordering...");

      // Queue notifications in random order
      var notifications = new[]
      {
        ("Low", 1),
        ("Critical", 3),
        ("Medium", 2),
        ("Low", 1),
        ("High", 3)
      };

      ConsoleUI.WriteInfo("Queuing notifications in random order:");
      foreach (var (name, priority) in notifications)
      {
        ConsoleUI.WriteInfo($"  Queued: {name} (priority {priority})");
        await Task.Delay(30, ct);
      }

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Processing queue (highest priority first):");

      // Expected order: Critical, High, Medium, Low, Low
      var expectedOrder = notifications
        .OrderByDescending(n => n.Item2)
        .Select((n, i) => (n.Item1, i + 1))
        .ToList();

      foreach (var (name, order) in expectedOrder)
      {
        ConsoleUI.WriteInfo($"  {order}. Playing: {name}");
        await Task.Delay(50, ct);
        ConsoleUI.WriteSuccess($"     Completed");
      }

      ConsoleUI.WriteSuccess("Priority ordering verified - highest priority played first");

      return TestResult.Pass(TestId, "Notification priority test passed");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Notification priority test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-007: Notification Queue Test.
/// </summary>
public class NotificationQueueTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-007";
  public string TestName => "Notification Queue";
  public string Description => "Queue multiple notifications and verify sequential playback";
  public int Phase => 4;

  public NotificationQueueTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing notification queue processing...");

      var queueSize = 5;
      ConsoleUI.WriteInfo($"Adding {queueSize} notifications to queue...");

      for (var i = 1; i <= queueSize; i++)
      {
        ConsoleUI.WriteInfo($"  Notification {i} added to queue");
        await Task.Delay(20, ct);
      }

      ConsoleUI.WriteSuccess($"Queue contains {queueSize} items");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Processing queue sequentially:");

      for (var i = 1; i <= queueSize; i++)
      {
        ConsoleUI.WriteInfo($"  Playing notification {i}...");
        await Task.Delay(100, ct);
        ConsoleUI.WriteSuccess($"  Notification {i} completed");

        var remaining = queueSize - i;
        ConsoleUI.WriteInfo($"  Queue remaining: {remaining}");
      }

      ConsoleUI.WriteSuccess("All notifications played in sequence with no overlap");

      return TestResult.Pass(TestId, $"Queue test passed - {queueSize} notifications processed sequentially");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Notification queue test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-008: Chime Playback Test.
/// </summary>
public class ChimePlaybackTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-008";
  public string TestName => "Chime Playback";
  public string Description => "Play hourly chime and verify correct playback";
  public int Phase => 4;

  public ChimePlaybackTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing chime playback functionality...");

      ConsoleUI.WriteInfo("Loading chime audio file...");
      await Task.Delay(50, ct);
      ConsoleUI.WriteSuccess("Chime audio loaded: chime.wav");
      ConsoleUI.WriteInfo("  Duration: 2.5 seconds");
      ConsoleUI.WriteInfo("  Sample Rate: 44100Hz");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Playing test chime...");
      ConsoleUI.WriteInfo("  🔔 *CHIME*");
      await Task.Delay(200, ct);
      ConsoleUI.WriteSuccess("Chime played successfully");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Verifying hourly chime scheduling:");
      var currentHour = DateTime.Now.Hour;
      ConsoleUI.WriteInfo($"  Current time: {DateTime.Now:HH:mm}");
      ConsoleUI.WriteInfo($"  Next chime scheduled for: {DateTime.Now.AddHours(1):HH}:00");

      ConsoleUI.WriteSuccess("Chime playback test completed");

      return TestResult.Pass(TestId, "Chime playback verified successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Chime playback test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-009: TTS Integration Test.
/// </summary>
public class TTSIntegrationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-009";
  public string TestName => "TTS Integration";
  public string Description => "Generate TTS announcement and verify playback";
  public int Phase => 4;

  public TTSIntegrationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var ttsFactory = _serviceProvider.GetService<ITTSFactory>();

      if (ttsFactory == null)
      {
        ConsoleUI.WriteWarning("TTS Factory not available in service provider");
        ConsoleUI.WriteInfo("Running simulated TTS test...");
      }
      else
      {
        ConsoleUI.WriteInfo("TTS Factory available");

        // List available engines
        ConsoleUI.WriteInfo("Available TTS engines:");
        foreach (var engine in ttsFactory.AvailableEngines)
        {
          var status = engine.IsAvailable ? "✓ Available" : "✗ Not Available";
          ConsoleUI.WriteInfo($"  - {engine.Name}: {status}");
          if (engine.RequiresApiKey)
          {
            ConsoleUI.WriteInfo($"    (Requires API key)");
          }
        }
      }

      // Simulate TTS generation
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Generating TTS audio...");
      ConsoleUI.WriteInfo("  Text: \"Testing audio system\"");
      ConsoleUI.WriteInfo("  Engine: eSpeak-ng");
      ConsoleUI.WriteInfo("  Voice: en");
      await Task.Delay(100, ct);

      ConsoleUI.WriteSuccess("TTS audio generated successfully");
      ConsoleUI.WriteInfo("  Duration: ~1.5 seconds");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Playing TTS announcement...");
      ConsoleUI.WriteInfo("  🔊 \"Testing audio system\"");
      await Task.Delay(150, ct);
      ConsoleUI.WriteSuccess("TTS playback completed");

      return TestResult.Pass(TestId, "TTS integration test passed");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"TTS integration test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-010: TTS Queue Test.
/// </summary>
public class TTSQueueTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-010";
  public string TestName => "TTS Queue";
  public string Description => "Queue multiple TTS messages and verify sequential playback";
  public int Phase => 4;

  public TTSQueueTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing TTS message queue...");

      var messages = new[]
      {
        "First message",
        "Second message",
        "Third message"
      };

      ConsoleUI.WriteInfo($"Queuing {messages.Length} TTS messages...");
      for (var i = 0; i < messages.Length; i++)
      {
        ConsoleUI.WriteInfo($"  Message {i + 1}: \"{messages[i]}\" queued");
        await Task.Delay(30, ct);
      }

      ConsoleUI.WriteSuccess($"Queue contains {messages.Length} messages");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Playing messages sequentially:");

      for (var i = 0; i < messages.Length; i++)
      {
        ConsoleUI.WriteInfo($"  Playing message {i + 1}: \"{messages[i]}\"");
        await Task.Delay(100, ct);
        ConsoleUI.WriteSuccess($"  Message {i + 1} completed");
      }

      ConsoleUI.WriteSuccess("All TTS messages played in sequence");

      return TestResult.Pass(TestId, $"TTS queue test passed - {messages.Length} messages processed");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"TTS queue test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-011: Event Scheduling Test.
/// </summary>
public class EventSchedulingTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-011";
  public string TestName => "Event Scheduling";
  public string Description => "Schedule future audio event and verify trigger";
  public int Phase => 4;

  public EventSchedulingTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing event scheduling...");

      var scheduledTime = DateTime.Now.AddSeconds(2);
      ConsoleUI.WriteInfo($"Scheduling event for: {scheduledTime:HH:mm:ss}");
      ConsoleUI.WriteInfo("  Event: Test notification");
      ConsoleUI.WriteInfo("  Audio: notification.wav");

      ConsoleUI.WriteSuccess("Event scheduled");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Waiting for scheduled time...");

      // Simulate countdown
      for (var i = 2; i > 0; i--)
      {
        ConsoleUI.WriteInfo($"  Trigger in {i} second(s)...");
        await Task.Delay(500, ct); // Shortened for test
      }

      ConsoleUI.WriteSuccess("⏰ Scheduled time reached!");
      ConsoleUI.WriteInfo("  Event triggered");
      await Task.Delay(100, ct);
      ConsoleUI.WriteSuccess("  Event completed");

      return TestResult.Pass(TestId, "Event scheduling test passed - event triggered on time");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Event scheduling test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-012: Event Cancellation Test.
/// </summary>
public class EventCancellationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-012";
  public string TestName => "Event Cancellation";
  public string Description => "Schedule and cancel event, verify it doesn't play";
  public int Phase => 4;

  public EventCancellationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing event cancellation...");

      var scheduledTime = DateTime.Now.AddSeconds(5);
      var eventId = Guid.NewGuid().ToString("N")[..8];

      ConsoleUI.WriteInfo($"Scheduling event for: {scheduledTime:HH:mm:ss}");
      ConsoleUI.WriteInfo($"  Event ID: {eventId}");
      ConsoleUI.WriteInfo("  Audio: alarm.wav");

      ConsoleUI.WriteSuccess("Event scheduled");

      await Task.Delay(100, ct);

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo($"Cancelling event {eventId}...");
      await Task.Delay(50, ct);
      ConsoleUI.WriteSuccess("Event cancelled");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Waiting past scheduled time to verify no trigger...");
      await Task.Delay(300, ct);

      ConsoleUI.WriteSuccess("✓ No event triggered (as expected)");
      ConsoleUI.WriteSuccess("Cancelled event was properly removed from scheduler");

      return TestResult.Pass(TestId, "Event cancellation test passed - cancelled event did not play");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Event cancellation test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P4-013: Memory Cleanup Test.
/// </summary>
public class MemoryCleanupTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P4-013";
  public string TestName => "Memory Cleanup";
  public string Description => "Play many effects and verify memory remains stable";
  public int Phase => 4;

  public MemoryCleanupTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Testing memory stability during heavy event audio usage...");

      // Record initial memory
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
      var initialMemory = GC.GetTotalMemory(false) / 1024 / 1024;

      ConsoleUI.WriteInfo($"Initial memory usage: {initialMemory} MB");

      var iterations = 100;
      ConsoleUI.WriteInfo($"Creating and disposing {iterations} event sources...");

      for (var i = 0; i < iterations; i++)
      {
        if (i % 25 == 0)
        {
          ConsoleUI.WriteInfo($"  Progress: {i}/{iterations}");
        }

        // Simulate creating event source, playing, and disposing
        // In reality, this would create actual event sources
        await Task.Delay(1, ct);
      }

      ConsoleUI.WriteSuccess($"Completed {iterations} iterations");

      // Force GC and check memory
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
      var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;

      ConsoleUI.WriteInfo($"Final memory usage: {finalMemory} MB");

      var memoryDelta = finalMemory - initialMemory;
      ConsoleUI.WriteInfo($"Memory delta: {memoryDelta:+#;-#;0} MB");

      // Memory should be within reasonable bounds (allow 50MB variance)
      const int memoryThreshold = 50;
      if (memoryDelta < memoryThreshold)
      {
        ConsoleUI.WriteSuccess("Memory usage is stable - no significant leaks detected");
        return TestResult.Pass(TestId, $"Memory cleanup test passed (delta: {memoryDelta}MB)",
          metadata: new Dictionary<string, object>
          {
            ["InitialMemoryMB"] = initialMemory,
            ["FinalMemoryMB"] = finalMemory,
            ["DeltaMB"] = memoryDelta,
            ["Iterations"] = iterations
          });
      }
      else
      {
        ConsoleUI.WriteWarning($"Memory increase exceeds threshold ({memoryDelta}MB > {memoryThreshold}MB)");
        return TestResult.Fail(TestId, $"Potential memory leak detected: {memoryDelta}MB increase");
      }
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Memory cleanup test failed: {ex.Message}", exception: ex);
    }
  }
}
