using Serilog;

namespace RTLSDRCore.DSP;

/// <summary>
/// Decodes RDS (Radio Data System) data from FM broadcast composite signals.
///
/// RDS is transmitted as a BPSK-modulated subcarrier at 57 kHz (3× the 19 kHz pilot).
/// This decoder extracts metadata from multiple RDS group types:
///   - Group 0A/0B: Program Service (PS) station name (8 chars)
///   - Group 2A/2B: Radio Text (RT) — 64-char free text (often "Artist - Title")
///   - All groups: PI code (station identifier), PTY (program type/genre)
///
/// Signal chain:
///   Composite FM → 57 kHz BPF → BPSK demod (3× PLL phase) → clock recovery (1187.5 baud)
///   → differential decode → block sync + CRC → group dispatch → metadata assembly
/// </summary>
public class RdsDecoder
{
  private static readonly ILogger Logger = Log.ForContext<RdsDecoder>();

  private readonly int _sampleRate;
  private readonly BandPassFilter _rdsBpf;
  private readonly LowPassFilter _basebandLpfI; // in-phase (data) channel
  private readonly LowPassFilter _basebandLpfQ; // quadrature channel (for Costas loop)

  // Costas loop carrier recovery for 57 kHz RDS subcarrier.
  // Tracks both phase and frequency independently.
  // Initialized from 3× PLL frequency, then free-runs with corrections.
  private float _costasPhase;       // carrier phase (radians)
  private float _costasFreq;        // carrier frequency (Hz)
  private bool _costasInitialized;
  private readonly float _costasAlpha; // proportional gain
  private readonly float _costasBeta;  // integral gain

  // Clock recovery NCO — runs at CHIP rate (2× baud) because RDS uses biphase coding.
  // Each data bit is encoded as two chips of opposite polarity (Manchester encoding).
  private const float BaudRate = 1187.5f;
  private const float ChipRate = BaudRate * 2f; // 2375 chips/sec
  private float _clockPhase;
  private float _clockPhaseStep;
  private float _integratorI;  // in-phase integrator (chip decisions)
  private float _previousSample; // for zero-crossing detection
  private int _previousSymbol;

  // Differential decoding
  private bool _hasPreviousSymbol;

  // Biphase (Manchester) decoding — each data bit = 2 chips of opposite polarity.
  // The data is encoded in the TRANSITION between consecutive chips.
  private float _prevChipValue;         // previous chip integrator output
  private int _chipClock;               // alternates 0/1 for clock/data chip detection
  private int _chipClockPolarity;       // which phase (0 or 1) carries data transitions
  private float _evenChipMagSum;        // magnitude sum for even-indexed chip transitions
  private float _oddChipMagSum;         // magnitude sum for odd-indexed chip transitions
  private int _chipWindowCount;         // chips counted in current polarity detection window
  private const int ChipWindowSize = 128; // chips per polarity detection cycle

  // Block sync: 26-bit shift register (16 data + 10 CRC/offset)
  private uint _shiftRegister;
  private int _bitsReceived;
  private SyncState _syncState = SyncState.Searching;
  private int _syncConfirmCount;
  private int _blockIndex; // 0=A, 1=B, 2=C, 3=D within a group
  private int _goodBlockRun;
  private int _badBlockCount;  // consecutive bad blocks in Synced state
  private const int SyncConfirmThreshold = 4; // consecutive good blocks to confirm sync
  private const int SyncLossThreshold = 8;    // consecutive bad blocks to lose sync

  // Diagnostics
  private int _totalBitsProcessed;
  private int _searchMatchCount;  // false matches in Searching
  private DateTime _lastDiagTime = DateTime.MinValue;
  private float _rdsSignalLevel;  // smoothed RDS subcarrier level after BPF
  private float _basebandLevel;   // smoothed baseband level after demod + LPF
  private float _symbolRate;      // measured symbols per second
  private int _symbolCount;       // symbols decoded since last diag
  private DateTime _lastSymbolCountTime = DateTime.MinValue;

  // Group assembly
  private readonly ushort[] _groupBlocks = new ushort[4]; // A, B, C, D data words

  // PS name assembly with noise rejection
  private readonly char[] _psChars = new char[8];
  private readonly int[] _psCharConfidence = new int[8]; // how many times each position confirmed
  private string? _confirmedStationName;
  private string? _candidateStationName;
  private int _candidateMatchCount;
  private const int PsConfirmThreshold = 2; // identical complete names required

  // Radio Text (Group 2A/2B) — 64-char free text, often "Artist - Title"
  private readonly char[] _rtChars = new char[64];
  private readonly bool[] _rtCharReceived = new bool[64];
  private bool _rtAbFlag;             // A/B flag — toggles on new message
  private bool _rtAbFlagInitialized;
  private string? _confirmedRadioText;
  private string? _candidateRadioText;
  private int _rtCandidateMatchCount;
  private const int RtConfirmThreshold = 2;

  // PI code (Program Identification) — unique station identifier
  private ushort? _piCode;
  private bool _piCodeLogged;

  // PTY (Program Type) — genre code 0-31
  private int _ptyCode = -1;
  private bool _ptyLogged;

  // CRC constants — RDS generator polynomial x^10+x^8+x^7+x^5+x^4+x^3+1
  private const ushort CrcPoly = 0x5B9;
  private static readonly ushort[] OffsetWords = { 0x0FC, 0x198, 0x168, 0x1B4 };
  private const ushort OffsetCPrime = 0x350;

  // Detection tracking
  private int _validBlockCount;
  private bool _syncAcquiredLogged;

  // PTY code to name table (North America RBDS)
  private static readonly string[] PtyNames =
  {
    "None", "News", "Information", "Sports", "Talk", "Rock", "Classic Rock",
    "Adult Hits", "Soft Rock", "Top 40", "Country", "Oldies", "Soft",
    "Nostalgia", "Jazz", "Classical", "R&B", "Soft R&B", "Language",
    "Religious Music", "Religious Talk", "Personality", "Public", "College",
    "Spanish Talk", "Spanish Music", "Hip Hop", "Unassigned", "Unassigned",
    "Weather", "Emergency Test", "Emergency"
  };

  /// <summary>
  /// The decoded 8-character Program Service station name, trimmed, or null if not yet decoded.
  /// </summary>
  public string? StationName => _confirmedStationName;

  /// <summary>
  /// Whether at least one valid RDS block has been decoded (indicates RDS signal present).
  /// </summary>
  public bool RdsDetected => _validBlockCount > 0;

  /// <summary>
  /// The decoded Radio Text (up to 64 characters), or null if not yet decoded.
  /// Often contains "Artist - Title" or station slogan.
  /// </summary>
  public string? RadioText => _confirmedRadioText;

  /// <summary>
  /// The PI (Program Identification) code, or null if not yet decoded.
  /// Unique 16-bit station identifier (e.g., 0x1234).
  /// </summary>
  public ushort? ProgramId => _piCode;

  /// <summary>
  /// The PTY (Program Type) code (0-31), or -1 if not yet decoded.
  /// Maps to genre names like "Rock", "News", "Classical", etc.
  /// </summary>
  public int ProgramType => _ptyCode;

  /// <summary>
  /// Human-readable name for the current PTY code, or null if not decoded.
  /// </summary>
  public string? ProgramTypeName => _ptyCode >= 0 && _ptyCode < PtyNames.Length
    ? PtyNames[_ptyCode] : null;

  /// <summary>
  /// Creates a new RDS decoder.
  /// </summary>
  /// <param name="sampleRate">Sample rate of the composite FM signal (e.g., 240000 Hz).</param>
  public RdsDecoder(int sampleRate)
  {
    _sampleRate = sampleRate;

    // 57 kHz BPF to isolate RDS subcarrier (55-59 kHz, 127 taps)
    _rdsBpf = new BandPassFilter(sampleRate, 55000f, 59000f, taps: 127);

    // Baseband LPFs after BPSK demod — I (data) and Q (Costas loop)
    _basebandLpfI = new LowPassFilter(sampleRate, 2400f, taps: 65);
    _basebandLpfQ = new LowPassFilter(sampleRate, 2400f, taps: 65);

    _clockPhaseStep = ChipRate / sampleRate; // ~2375/240000 ≈ 101 samples/chip

    // Costas loop: locks to the RDS subcarrier's actual phase.
    // Bandwidth ~30 Hz — fast enough to acquire within ~15ms,
    // narrow enough not to track data transitions at 1187.5 baud.
    var costasOmega = 2.0f * MathF.PI * 30f / sampleRate;
    var costasDamping = 0.707f; // critically damped
    _costasAlpha = 2.0f * costasDamping * costasOmega;
    _costasBeta = costasOmega * costasOmega;
  }

  /// <summary>
  /// Processes a block of FM composite samples, extracting RDS data.
  /// Must be called after StereoFmDecoder.Decode() so PLL phase is current.
  /// </summary>
  /// <param name="composite">FM composite signal at the demod sample rate.</param>
  /// <param name="count">Number of samples to process.</param>
  /// <param name="pllPhase">Current PLL phase from StereoFmDecoder (radians, 0 to 2π).</param>
  /// <param name="pllFrequency">Current PLL frequency from StereoFmDecoder (~19 kHz).</param>
  public void Process(ReadOnlySpan<float> composite, int count, float pllPhase, float pllFrequency)
  {
    // Initialize Costas loop frequency from the stereo PLL (once).
    // After that, the Costas loop tracks both frequency and phase independently.
    if (!_costasInitialized)
    {
      _costasFreq = pllFrequency * 3.0f; // ~57000 Hz
      _costasInitialized = true;
    }

    for (int i = 0; i < count; i++)
    {
      // 1. BPF to isolate 57 kHz RDS subcarrier
      var filtered = _rdsBpf.Process(composite[i]);

      // Track RDS subcarrier signal level (smoothed absolute value)
      _rdsSignalLevel += 0.0001f * (MathF.Abs(filtered) - _rdsSignalLevel);

      // 2. Costas loop BPSK demodulation.
      // The Costas loop simultaneously tracks carrier phase AND frequency,
      // independent of the stereo PLL. This avoids phase ambiguity issues
      // from the 3× PLL phase multiplication.
      var demodI = filtered * MathF.Cos(_costasPhase);
      var demodQ = filtered * -MathF.Sin(_costasPhase);

      // 3. Baseband LPFs to remove double-frequency components
      var basebandI = _basebandLpfI.Process(demodI);
      var basebandQ = _basebandLpfQ.Process(demodQ);

      // Track baseband I-channel signal level
      _basebandLevel += 0.0001f * (MathF.Abs(basebandI) - _basebandLevel);

      // 4. Costas loop error: normalized I*Q for BPSK.
      // sin(2δ)/2 where δ = phase error. Drives δ → 0 or π (both valid for BPSK).
      var power = basebandI * basebandI + basebandQ * basebandQ;
      var phaseError = power > 1e-10f
        ? basebandI * basebandQ / power
        : 0f;

      // Update Costas loop (2nd order PLL)
      _costasFreq += _costasBeta * phaseError;
      _costasPhase += 2.0f * MathF.PI * _costasFreq / _sampleRate
                      + _costasAlpha * phaseError;

      // Keep phase in [0, 2π)
      if (_costasPhase > 2.0f * MathF.PI) _costasPhase -= 2.0f * MathF.PI;
      else if (_costasPhase < 0) _costasPhase += 2.0f * MathF.PI;

      // 5. Clock recovery + symbol decision (uses I channel — the data channel)
      ProcessClockRecovery(basebandI);
    }

    // Periodic diagnostic logging (every 30 seconds)
    var now = DateTime.UtcNow;
    if ((now - _lastDiagTime).TotalSeconds >= 30)
    {
      if (_lastSymbolCountTime != DateTime.MinValue)
      {
        var elapsed = (now - _lastSymbolCountTime).TotalSeconds;
        _symbolRate = elapsed > 0 ? (float)(_symbolCount / elapsed) : 0;
      }
      _lastSymbolCountTime = now;
      _symbolCount = 0;
      _lastDiagTime = now;
      Logger.Debug(
        "RDS diag: bpfLevel={BpfLevel:F6}, basebandLevel={BasebandLevel:F6}, " +
        "symbolRate={SymbolRate:F1}/s, syncState={SyncState}, " +
        "validBlocks={ValidBlocks}, searchMatches={SearchMatches}, " +
        "costasFreq={CostasFreq:F1} Hz, costasPhase={CostasPhase:F3} rad",
        _rdsSignalLevel, _basebandLevel,
        _symbolRate, _syncState,
        _validBlockCount, _searchMatchCount,
        _costasFreq, _costasPhase);
    }
  }

  /// <summary>
  /// Resets all decoder state. Call when changing frequency.
  /// </summary>
  public void Reset()
  {
    _rdsBpf.Reset();
    _basebandLpfI.Reset();
    _basebandLpfQ.Reset();
    _costasPhase = 0;
    _costasFreq = 57000f;
    _costasInitialized = false;
    _clockPhase = 0;
    _integratorI = 0;
    _previousSample = 0;
    _previousSymbol = 0;
    _hasPreviousSymbol = false;
    _prevChipValue = 0;
    _chipClock = 0;
    _chipClockPolarity = 0;
    _evenChipMagSum = 0;
    _oddChipMagSum = 0;
    _chipWindowCount = 0;
    _shiftRegister = 0;
    _bitsReceived = 0;
    _syncState = SyncState.Searching;
    _syncConfirmCount = 0;
    _blockIndex = 0;
    _goodBlockRun = 0;
    _badBlockCount = 0;
    _totalBitsProcessed = 0;
    _searchMatchCount = 0;
    _rdsSignalLevel = 0;
    _basebandLevel = 0;
    _symbolRate = 0;
    _symbolCount = 0;
    _lastDiagTime = DateTime.MinValue;
    _lastSymbolCountTime = DateTime.MinValue;
    Array.Clear(_groupBlocks);
    Array.Clear(_psChars);
    Array.Clear(_psCharConfidence);
    _confirmedStationName = null;
    _candidateStationName = null;
    _candidateMatchCount = 0;
    Array.Clear(_rtChars);
    Array.Clear(_rtCharReceived);
    _rtAbFlag = false;
    _rtAbFlagInitialized = false;
    _confirmedRadioText = null;
    _candidateRadioText = null;
    _rtCandidateMatchCount = 0;
    _piCode = null;
    _piCodeLogged = false;
    _ptyCode = -1;
    _ptyLogged = false;
    _validBlockCount = 0;
    _syncAcquiredLogged = false;
  }

  private void ProcessClockRecovery(float sample)
  {
    // Zero-crossing based timing adjustment.
    // Transitions should align with chip boundaries (clockPhase ≈ 0).
    if ((_previousSample > 0 && sample <= 0) || (_previousSample < 0 && sample >= 0))
    {
      // Zero crossing = chip boundary → clock phase should be near 0 (not 0.5)
      var phaseError = _clockPhase > 0.5f ? (_clockPhase - 1.0f) : _clockPhase;
      _clockPhase -= phaseError * 0.05f; // gentle correction
    }

    // Accumulate for matched filter (integrate over one chip period)
    _integratorI += sample;

    // Advance clock at chip rate (2375 Hz = 2× baud rate)
    _clockPhase += _clockPhaseStep;

    if (_clockPhase >= 1.0f)
    {
      // Chip boundary — dump integrator and decode
      _clockPhase -= 1.0f;
      var chipValue = _integratorI;
      _integratorI = 0;

      ProcessChip(chipValue);
    }

    _previousSample = sample;
  }

  private void ProcessChip(float chipValue)
  {
    // Biphase (Manchester) decoding:
    // Each RDS data bit is encoded as two chips with opposite polarity.
    // The data is carried by the TRANSITION between consecutive chips.
    // diff = chip[n] - chip[n-1]: large magnitude at data edges, small at clock edges.
    var biphaseValue = chipValue - _prevChipValue;
    _prevChipValue = chipValue;

    // Track transition magnitudes to detect clock polarity.
    // Data transitions have consistently larger magnitudes than clock transitions.
    var mag = MathF.Abs(biphaseValue);
    if (_chipClock % 2 == 0)
      _evenChipMagSum += mag;
    else
      _oddChipMagSum += mag;

    _chipWindowCount++;
    if (_chipWindowCount >= ChipWindowSize)
    {
      // Require 20% margin to switch polarity (avoid flapping)
      if (_evenChipMagSum > _oddChipMagSum * 1.2f)
        _chipClockPolarity = 0;
      else if (_oddChipMagSum > _evenChipMagSum * 1.2f)
        _chipClockPolarity = 1;
      _evenChipMagSum = 0;
      _oddChipMagSum = 0;
      _chipWindowCount = 0;
    }

    // Output a data bit only on the data-carrying phase
    if (_chipClock % 2 == _chipClockPolarity)
    {
      _symbolCount++; // count data bits for rate diagnostics
      var symbol = biphaseValue >= 0 ? 1 : 0;

      // Differential decoding: bit = currentSymbol XOR previousSymbol
      if (_hasPreviousSymbol)
      {
        var bit = symbol ^ _previousSymbol;
        ProcessBit(bit);
      }
      _previousSymbol = symbol;
      _hasPreviousSymbol = true;
    }

    _chipClock = (_chipClock + 1) % 2;
  }

  private void ProcessBit(int bit)
  {
    // Shift in the new bit
    _shiftRegister = ((_shiftRegister << 1) | (uint)bit) & 0x03FFFFFF; // 26 bits
    _bitsReceived++;
    _totalBitsProcessed++;

    switch (_syncState)
    {
      case SyncState.Searching:
        // Try all four offset words to find sync (checks every bit position)
        if (_bitsReceived >= 26)
        {
          for (int offset = 0; offset < 4; offset++)
          {
            if (CheckSyndrome(_shiftRegister, offset))
            {
              _searchMatchCount++;
              _syncState = SyncState.Confirming;
              _blockIndex = (offset + 1) % 4; // next expected block
              _syncConfirmCount = 1;
              _goodBlockRun = 1;
              _bitsReceived = 0;
              // Store the block data but do NOT call ProcessGroup —
              // we don't have enough confirmed blocks yet
              StoreBlockData(offset);
              Logger.Debug("RDS: Potential sync at block {Block}, entering Confirming", offset);
              return;
            }
          }
        }
        break;

      case SyncState.Confirming:
        if (_bitsReceived >= 26)
        {
          _bitsReceived = 0;
          if (CheckSyndrome(_shiftRegister, _blockIndex))
          {
            StoreBlockData(_blockIndex);
            _syncConfirmCount++;
            _goodBlockRun++;
            if (_syncConfirmCount >= SyncConfirmThreshold)
            {
              _syncState = SyncState.Synced;
              _badBlockCount = 0;  // fresh counter for synced state
              _goodBlockRun = 0;
              if (!_syncAcquiredLogged)
              {
                _syncAcquiredLogged = true;
                Logger.Information("RDS: Block sync acquired after {Confirms} confirmed blocks " +
                  "(total valid={ValidBlocks}, searchMatches={SearchMatches})",
                  _syncConfirmCount, _validBlockCount, _searchMatchCount);
              }
              else
              {
                Logger.Debug("RDS: Block sync re-acquired");
              }
            }
          }
          else
          {
            // Bad block — reset to searching
            Logger.Debug("RDS: Confirming failed at block {Block} (had {Count} good), back to Searching",
              _blockIndex, _syncConfirmCount);
            _syncState = SyncState.Searching;
            _syncConfirmCount = 0;
            _goodBlockRun = 0;
          }
          _blockIndex = (_blockIndex + 1) % 4;
        }
        break;

      case SyncState.Synced:
        if (_bitsReceived >= 26)
        {
          _bitsReceived = 0;
          if (CheckSyndrome(_shiftRegister, _blockIndex))
          {
            StoreBlockData(_blockIndex);
            _goodBlockRun++;
            _badBlockCount = 0; // reset consecutive bad block counter

            // Only process groups in Synced state to avoid stale data.
            // Block D (index 3) completes a group.
            if (_blockIndex == 3)
            {
              ProcessGroup();
            }
          }
          else
          {
            _goodBlockRun = 0;
            _badBlockCount++;
            if (_badBlockCount >= SyncLossThreshold)
            {
              Logger.Information("RDS: Block sync lost after {Failures} consecutive bad blocks", _badBlockCount);
              _syncState = SyncState.Searching;
              _syncConfirmCount = 0;
              _badBlockCount = 0;
            }
          }
          _blockIndex = (_blockIndex + 1) % 4;
        }
        break;
    }
  }

  private void StoreBlockData(int blockIndex)
  {
    _validBlockCount++;
    var dataWord = (ushort)(_shiftRegister >> 10); // upper 16 bits are data
    _groupBlocks[blockIndex] = dataWord;
  }

  private void UpdatePiCode(ushort piCode)
  {
    if (_piCode != piCode)
    {
      _piCode = piCode;
      if (!_piCodeLogged)
      {
        _piCodeLogged = true;
        Logger.Information("RDS: PI code = 0x{PiCode:X4}", piCode);
      }
    }
  }

  private void ProcessGroup()
  {
    // Block A carries PI code
    UpdatePiCode(_groupBlocks[0]);

    var blockB = _groupBlocks[1];
    var groupType = (blockB >> 12) & 0x0F;     // bits 15-12: group type (0-15)
    var versionB = ((blockB >> 11) & 0x01) == 1; // bit 11: 0=A, 1=B

    // PTY is in bits 10-6 of block B (all group types)
    var pty = (blockB >> 5) & 0x1F;
    UpdatePty((int)pty);

    var groupLabel = $"{groupType}{(versionB ? "B" : "A")}";

    switch (groupType)
    {
      case 0:
        // Group 0A/0B: Program Service name
        ProcessGroup0PS(blockB, _groupBlocks[3]);
        break;
      case 2:
        // Group 2A/2B: Radio Text
        ProcessGroup2RT(blockB, _groupBlocks[2], _groupBlocks[3], versionB);
        break;
      default:
        Logger.Debug("RDS: Received group {Group} (not decoded)", groupLabel);
        break;
    }
  }

  private void UpdatePty(int pty)
  {
    if (_ptyCode != pty)
    {
      _ptyCode = pty;
      var name = pty >= 0 && pty < PtyNames.Length ? PtyNames[pty] : "Unknown";
      if (!_ptyLogged || pty != 0) // always log non-zero PTY changes
      {
        _ptyLogged = true;
        Logger.Information("RDS: PTY = {PtyCode} ({PtyName})", pty, name);
      }
    }
  }

  private void ProcessGroup0PS(ushort blockB, ushort blockD)
  {
    // Block B bits 1-0: character position index (0-3, each giving 2 chars)
    var charIndex = blockB & 0x03;
    var pos = charIndex * 2;

    // Block D contains two PS characters (high byte = first char, low byte = second)
    var char1 = (char)((blockD >> 8) & 0xFF);
    var char2 = (char)(blockD & 0xFF);

    // Validate: printable ASCII range (0x20-0x7E)
    if (char1 >= 0x20 && char1 <= 0x7E && char2 >= 0x20 && char2 <= 0x7E)
    {
      _psChars[pos] = char1;
      _psChars[pos + 1] = char2;
      _psCharConfidence[pos]++;
      _psCharConfidence[pos + 1]++;

      // Check if all 4 positions have been received at least once
      if (_psCharConfidence[0] > 0 && _psCharConfidence[2] > 0 &&
          _psCharConfidence[4] > 0 && _psCharConfidence[6] > 0)
      {
        var name = new string(_psChars).Trim();
        if (!string.IsNullOrEmpty(name))
        {
          TryConfirmStationName(name);
        }
      }
    }
  }

  private void TryConfirmStationName(string name)
  {
    if (name == _candidateStationName)
    {
      _candidateMatchCount++;
      if (_candidateMatchCount >= PsConfirmThreshold && _confirmedStationName != name)
      {
        var oldName = _confirmedStationName;
        _confirmedStationName = name;
        Logger.Information("RDS: Station name = \"{StationName}\" (PI=0x{PiCode:X4})",
          name, _piCode ?? 0);
        if (oldName != null)
        {
          Logger.Debug("RDS: Station name changed from \"{OldName}\" to \"{NewName}\"", oldName, name);
        }
      }
    }
    else
    {
      _candidateStationName = name;
      _candidateMatchCount = 1;
    }
  }

  private void ProcessGroup2RT(ushort blockB, ushort blockC, ushort blockD, bool versionB)
  {
    // A/B flag in bit 4 of block B — toggles when station sends new message
    var abFlag = ((blockB >> 4) & 0x01) == 1;
    if (_rtAbFlagInitialized && abFlag != _rtAbFlag)
    {
      // New RT message — clear buffer
      Array.Clear(_rtChars);
      Array.Clear(_rtCharReceived);
      Logger.Debug("RDS: Radio Text A/B flag toggled, clearing RT buffer");
    }
    _rtAbFlag = abFlag;
    _rtAbFlagInitialized = true;

    // Block B bits 3-0: text segment address
    var segmentAddr = blockB & 0x0F;

    if (versionB)
    {
      // Group 2B: 2 chars from block D only (block C carries PI code repeat)
      var pos = segmentAddr * 2;
      if (pos + 1 < 64)
      {
        SetRtChar(pos, (char)((blockD >> 8) & 0xFF));
        SetRtChar(pos + 1, (char)(blockD & 0xFF));
      }
    }
    else
    {
      // Group 2A: 4 chars from blocks C and D
      var pos = segmentAddr * 4;
      if (pos + 3 < 64)
      {
        SetRtChar(pos, (char)((blockC >> 8) & 0xFF));
        SetRtChar(pos + 1, (char)(blockC & 0xFF));
        SetRtChar(pos + 2, (char)((blockD >> 8) & 0xFF));
        SetRtChar(pos + 3, (char)(blockD & 0xFF));
      }
    }

    // Try to assemble a complete Radio Text message
    TryAssembleRadioText();
  }

  private void SetRtChar(int pos, char c)
  {
    // 0x0D = carriage return, marks end of message (fill remaining with spaces)
    if (c == 0x0D)
    {
      for (int i = pos; i < 64; i++)
      {
        _rtChars[i] = ' ';
        _rtCharReceived[i] = true;
      }
      return;
    }

    // Validate printable ASCII
    if (c >= 0x20 && c <= 0x7E)
    {
      _rtChars[pos] = c;
      _rtCharReceived[pos] = true;
    }
  }

  private void TryAssembleRadioText()
  {
    // Check if we have a contiguous run from position 0
    int length = 0;
    for (int i = 0; i < 64; i++)
    {
      if (!_rtCharReceived[i]) break;
      length = i + 1;
    }

    // Need at least 4 characters to be meaningful
    if (length < 4) return;

    var text = new string(_rtChars, 0, length).Trim();
    if (string.IsNullOrEmpty(text)) return;

    if (text == _candidateRadioText)
    {
      _rtCandidateMatchCount++;
      if (_rtCandidateMatchCount >= RtConfirmThreshold && _confirmedRadioText != text)
      {
        _confirmedRadioText = text;
        Logger.Information("RDS: Radio Text = \"{RadioText}\"", text);
      }
    }
    else
    {
      _candidateRadioText = text;
      _rtCandidateMatchCount = 1;
    }
  }

  /// <summary>
  /// Computes the CRC syndrome of a 26-bit RDS word and checks it against
  /// the expected offset word for the given block position.
  /// </summary>
  public static bool CheckSyndrome(uint word26, int blockIndex)
  {
    var syndrome = ComputeSyndrome(word26);
    var expectedOffset = blockIndex == 2
      ? OffsetWords[2] // Use offset C for version A; C' handled separately
      : OffsetWords[blockIndex];

    if (syndrome == expectedOffset) return true;

    // Also check offset C' for block 2 (Group version B)
    if (blockIndex == 2 && syndrome == OffsetCPrime) return true;

    return false;
  }

  /// <summary>
  /// Computes the 10-bit CRC syndrome of a 26-bit RDS word using the
  /// generator polynomial G(x) = x^10 + x^8 + x^7 + x^5 + x^4 + x^3 + 1.
  /// </summary>
  public static ushort ComputeSyndrome(uint word26)
  {
    // Process 26 bits through the CRC register
    uint reg = 0;
    for (int i = 25; i >= 0; i--)
    {
      var bit = (word26 >> i) & 1;
      var feedback = (reg >> 9) & 1; // MSB of 10-bit register
      reg = ((reg << 1) | bit) & 0x3FF;
      if (feedback == 1)
      {
        reg ^= CrcPoly;
      }
    }
    return (ushort)(reg & 0x3FF);
  }

  private enum SyncState
  {
    Searching,
    Confirming,
    Synced
  }
}
