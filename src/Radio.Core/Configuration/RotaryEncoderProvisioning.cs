namespace Radio.Core.Configuration;

/// <summary>Whether the device agreed with one configured field.</summary>
public enum RotaryEncoderFieldAgreement
{
  /// <summary>No read-back has been obtained on this connection. <b>Not the same as agreement.</b></summary>
  NotReadBack = 0,

  /// <summary>The device reported this field back with the value that was pushed.</summary>
  Agrees = 1,

  /// <summary>The device reported a different value. <see cref="RotaryEncoderFieldState.ReadBackValue"/> carries it.</summary>
  Differs = 2,
}

/// <summary>One field of the device configuration, as designed and as the device reports it.</summary>
/// <param name="EncoderIndex">Encoder index, or <c>-1</c> for the global <c>steps_per_detent</c>.</param>
/// <param name="Field">The wire field name, matching the strings <c>RotaryEncoderConfigVerifier.Compare</c> emits.</param>
/// <param name="DesignedValue">What the app pushed, rendered for display.</param>
/// <param name="ReadBackValue">What the device reported, or null when there has been no read-back.</param>
/// <param name="IsSafetyField">
/// <c>wrap</c> on VOLUME and <c>reverse</c> on any knob. A mismatch here is a hard fault immediately
/// and tightens the host volume clamp; the page shows it differently for that reason.
/// </param>
/// <param name="Agreement">Whether the device echoed this field back with the pushed value.</param>
public sealed record RotaryEncoderFieldState(
  int EncoderIndex,
  string Field,
  string DesignedValue,
  string? ReadBackValue,
  bool IsSafetyField,
  RotaryEncoderFieldAgreement Agreement);

/// <summary>How the device's flash compares to the configuration the app would push right now.</summary>
public enum RotaryEncoderFlashState
{
  /// <summary>This app has never flashed this device. Renders as <c>never saved</c> — not as a warning.</summary>
  NeverSaved = 0,

  /// <summary>The bytes last flashed are byte-identical to the bytes the app would push now.</summary>
  MatchesCurrentDesign = 1,

  /// <summary>The flashed bytes differ. The knobs still run the pushed config; only a boot window before
  /// the app pushes would use the stale copy.</summary>
  DiffersFromCurrentDesign = 2,
}

/// <summary>
/// Everything the encoder Settings surface renders, in one immutable read (ENC-8).
///
/// <para>
/// ⚠ Every field here is an <b>assertion about state that the page will print</b>, so each one must
/// be produced by a check rather than an inference. <see cref="LastVerifiedUtc"/> is set only when a
/// read-back <i>matched</i>, never when a push was merely attempted; <see cref="Flash"/> is a
/// comparison of stored bytes against current bytes, not a guess from a timestamp.
/// </para>
/// </summary>
public sealed record RotaryEncoderProvisioningSnapshot
{
  /// <summary>False when <c>RotaryEncoder:Enabled</c> is off. The page then says so and shows nothing else.</summary>
  public bool Enabled { get; init; }

  /// <summary>True while the HID device is open.</summary>
  public bool IsConnected { get; init; }

  /// <summary>True once the device has connected at least once since the API started (ENC-0).</summary>
  public bool WasEverConnected { get; init; }

  /// <summary>The tier the last verified push landed in.</summary>
  public RotaryEncoderConfigStatus Status { get; init; } = RotaryEncoderConfigStatus.Unknown;

  /// <summary>When a read-back last <b>matched</b> the pushed configuration. Null if that has never happened.</summary>
  public DateTimeOffset? LastVerifiedUtc { get; init; }

  /// <summary>When a push was last attempted, whatever its outcome.</summary>
  public DateTimeOffset? LastAttemptedUtc { get; init; }

  /// <summary>When this app last successfully wrote the device's flash. Persisted, because flash outlives a restart.</summary>
  public DateTimeOffset? LastSavedToDeviceUtc { get; init; }

  /// <summary>How the flashed bytes compare to the bytes the app would push now.</summary>
  public RotaryEncoderFlashState Flash { get; init; } = RotaryEncoderFlashState.NeverSaved;

  /// <summary>Every comparable field, designed value beside read-back value.</summary>
  public IReadOnlyList<RotaryEncoderFieldState> Fields { get; init; } = [];
}

/// <summary>
/// The owner-initiated half of encoder configuration (ENC-8), separate from
/// <see cref="Radio.Core.Interfaces.Input.IRotaryEncoderService"/> so the input path is not widened
/// with provisioning concerns. The same <c>HidRotaryEncoderService</c> instance implements both.
/// </summary>
public interface IRotaryEncoderProvisioning
{
  /// <summary>Current state. Cheap, allocation-only — safe to poll at 2 Hz while the page is open.</summary>
  RotaryEncoderProvisioningSnapshot GetSnapshot();

  /// <summary>
  /// Pushes the resolved configuration and verifies it by read-back. <b>Does not touch flash.</b>
  /// This is the Settings page's <c>Re-apply settings</c>.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> ReapplyAsync(CancellationToken ct = default);

  /// <summary>
  /// Pushes, verifies, and only then writes flash (<c>0x03/0x01</c>), recording what was written.
  /// <b>Flash receives exactly the bytes that were just verified</b> — see the plan §0.5.
  /// A failed verify leaves flash untouched.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> SaveToDeviceAsync(CancellationToken ct = default);

  /// <summary>
  /// Requests that the device zero its movement/diagnostic counters (<c>0x03/0x05</c>).
  ///
  /// <para>
  /// The protocol has <b>no acknowledgement</b> for this command and this build has no diagnostics
  /// decoder, so the returned value reports only that the command was <b>sent</b> - never that the
  /// counters are zero. Callers must not upgrade that claim in their own copy.
  /// </para>
  /// </summary>
  Task<bool> ResetCountersAsync(CancellationToken ct = default);

  /// <summary>
  /// Persists a per-knob direction override and immediately pushes + verifies it. Marks the flashed
  /// copy stale as a consequence of the push, not as a separate assertion.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> SetReverseAsync(int encoderIndex, bool reverse, CancellationToken ct = default);
}
