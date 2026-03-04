namespace Radio.Core.Interfaces;

/// <summary>
/// Abstraction for sleep/standby mode management.
/// Lives in Core so Infrastructure (e.g., RotaryEncoderActionRouter) can
/// depend on it without referencing Radio.API.
/// </summary>
public interface ISleepService
{
  bool IsSleeping { get; }
  Task EnterSleepAsync();
  Task WakeAsync(string wakeSource = "unknown");
}
