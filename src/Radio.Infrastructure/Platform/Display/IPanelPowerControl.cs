namespace Radio.Infrastructure.Platform.Display;

/// <summary>
/// The one operation <see cref="PanelPowerService"/> needs from the platform: set the panel's power.
/// A seam so the service's safety rules can be tested without a compositor.
/// </summary>
public interface IPanelPowerControl
{
  /// <summary>
  /// Powers the panel on or off. Never throws.
  /// </summary>
  /// <returns>True when the platform confirmed the change; false when it did not.</returns>
  Task<bool> SetPanelPowerAsync(bool on, CancellationToken cancellationToken = default);
}
