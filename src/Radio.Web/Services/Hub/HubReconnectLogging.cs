using Microsoft.Extensions.Logging;

namespace Radio.Web.Services.Hub;

/// <summary>
/// UI-10: the Warning written when a hub connection starts reconnecting.
/// </summary>
/// <remarks>
/// A reconnect is worth a Warning. But the common cause, the client's <c>ServerTimeout</c> elapsing, is a
/// <see cref="TimeoutException"/> whose stack trace says nothing the message does not. radio-web's
/// journal is Information-level and every stack is ~10 lines there (CLAUDE.md § Services), and before
/// UI-10 this fired 535 times a day. So a timeout logs its message only; anything else keeps the full
/// exception.
/// </remarks>
internal static class HubReconnectLogging
{
  public static void LogReconnecting(ILogger logger, Exception? exception, string hubName)
  {
    if (exception is TimeoutException timeout)
    {
      logger.LogWarning("{Hub} hub reconnecting: {Reason}", hubName, timeout.Message);
    }
    else
    {
      logger.LogWarning(exception, "{Hub} hub reconnecting", hubName);
    }
  }
}
