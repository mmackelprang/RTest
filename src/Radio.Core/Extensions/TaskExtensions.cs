using Microsoft.Extensions.Logging;

namespace Radio.Core.Extensions;

/// <summary>
/// Extension methods for Task to handle fire-and-forget patterns safely.
/// </summary>
public static class TaskExtensions
{
  /// <summary>
  /// Observes a fire-and-forget task, logging any exceptions that occur.
  /// Use this instead of <c>_ = SomeMethodAsync()</c> to prevent silent exception loss.
  /// </summary>
  /// <param name="task">The task to observe.</param>
  /// <param name="logger">Logger to record any exceptions.</param>
  /// <param name="context">A short description of what the task does, for log messages.</param>
  public static async void SafeFireAndForget(this Task task, ILogger logger, string context)
  {
    try
    {
      await task;
    }
    catch (OperationCanceledException)
    {
      // Expected during shutdown — don't log as error
      logger.LogDebug("Fire-and-forget task cancelled: {Context}", context);
    }
    catch (ObjectDisposedException)
    {
      // Expected during disposal — don't log as error
      logger.LogDebug("Fire-and-forget task hit disposed object: {Context}", context);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unhandled exception in fire-and-forget task: {Context}", context);
    }
  }
}
