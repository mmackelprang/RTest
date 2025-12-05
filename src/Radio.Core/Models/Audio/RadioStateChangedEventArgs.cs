using System;

namespace Radio.Core.Models.Audio;

/// <summary>
/// Event arguments for radio state change events.
/// </summary>
public class RadioStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the name of the property that changed.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the new value of the property.
    /// </summary>
    public object? NewValue { get; }

    /// <summary>
    /// Gets the old value of the property.
    /// </summary>
    public object? OldValue { get; }

    /// <summary>
    /// Gets the timestamp of the change.
    /// </summary>
    public DateTime Timestamp { get; }

    public RadioStateChangedEventArgs(string propertyName, object? newValue, object? oldValue = null)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        NewValue = newValue;
        OldValue = oldValue;
        Timestamp = DateTime.Now;
    }
}
