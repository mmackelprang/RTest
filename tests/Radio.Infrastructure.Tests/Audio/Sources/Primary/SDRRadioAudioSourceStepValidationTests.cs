using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-102: <see cref="SDRRadioAudioSource.SetFrequencyStepAsync"/> accepts only the current band's
/// allowed steps, and the start-up restore falls back to the band default for a saved step the band
/// does not allow. The receiver runs on the mock device and is never started, so nothing streams.
/// </summary>
public sealed class SDRRadioAudioSourceStepValidationTests : IDisposable
{
  private readonly RadioReceiver _receiver = RadioReceiver.CreateWithMockDevice();
  private readonly Mock<ILogger<SDRRadioAudioSource>> _logger = new();

  public void Dispose() => _receiver.Dispose();

  private SDRRadioAudioSource CreateSource(IConfigurationManager? configurationManager = null)
  {
    _logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    return new SDRRadioAudioSource(
      _logger.Object, _receiver, options.Object, configurationManager: configurationManager);
  }

  [Theory]
  [InlineData(BandType.FM, 100_100_000L, 7L)]       // the dossier's example: any positive step was accepted
  [InlineData(BandType.FM, 100_100_000L, 25_000L)]
  [InlineData(BandType.AM, 1_010_000L, 5_000L)]     // AM keeps {1, 9, 10} kHz (owner: no 5 kHz)
  [InlineData(BandType.Shortwave, 5_905_000L, 10_000L)]
  [InlineData(BandType.Aircraft, 121_500_000L, 12_500L)]
  public async Task StepNotInTheBandsList_IsRefused_WithTheAllowedList_AndTheStepIsUnchanged(
    BandType bandType, long hz, long stepHz)
  {
    Assert.True(_receiver.SetBand(bandType, hz));
    SDRRadioAudioSource source = CreateSource();
    long before = source.FrequencyStep.Hertz;
    RTLSDRCore.Models.RadioBand band = RTLSDRCore.Bands.BandPresets.GetBand(bandType);

    FrequencyStepNotAllowedException ex = await Assert.ThrowsAsync<FrequencyStepNotAllowedException>(
      () => source.SetFrequencyStepAsync(new Frequency(stepHz)));

    Assert.Equal(band.AllowedStepSizes, ex.AllowedStepsHz);
    Assert.Equal(stepHz, ex.StepHz);
    Assert.Equal(band.Name, ex.BandName);
    Assert.IsAssignableFrom<ArgumentOutOfRangeException>(ex); // callers that catch the old type still do
    Assert.Equal(before, source.FrequencyStep.Hertz);
  }

  [Theory]
  [InlineData(BandType.FM, 100_100_000L)]
  [InlineData(BandType.AM, 1_010_000L)]
  [InlineData(BandType.Shortwave, 5_905_000L)]
  [InlineData(BandType.Aircraft, 121_500_000L)]
  [InlineData(BandType.Weather, 162_475_000L)]
  [InlineData(BandType.VHF, 146_520_000L)]
  public async Task EveryAllowedStep_IsAccepted(BandType bandType, long hz)
  {
    Assert.True(_receiver.SetBand(bandType, hz));
    SDRRadioAudioSource source = CreateSource();

    foreach (long step in RTLSDRCore.Bands.BandPresets.GetBand(bandType).AllowedStepSizes)
    {
      await source.SetFrequencyStepAsync(new Frequency(step));
      Assert.Equal(step, source.FrequencyStep.Hertz);
    }
  }

  [Fact]
  public async Task NonPositiveStep_IsStillTheOldArgumentOutOfRange()
  {
    SDRRadioAudioSource source = CreateSource();

    ArgumentOutOfRangeException ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.SetFrequencyStepAsync(new Frequency(0)));

    Assert.IsNotType<FrequencyStepNotAllowedException>(ex);
  }

  [Fact]
  public async Task Aud95_AFrequencyOnlyTuneIntoAnotherBand_KeepsTheOldStep_AndNothingThrows()
  {
    // AUD-102 does not absorb AUD-95: the old band's step survives a frequency-only band change.
    // What this row guarantees is that validation does not make that path throw: the kept step is
    // still used for stepping, and the next step change is validated against the new band.
    Assert.True(_receiver.SetBand(BandType.Weather, 162_400_000));
    SDRRadioAudioSource source = CreateSource();
    await source.SetFrequencyStepAsync(new Frequency(25_000)); // one of WB's steps

    await source.SetFrequencyAsync(new Frequency(101_500_000));

    Assert.Equal(BandType.FM, _receiver.CurrentBand.Type);
    Assert.Equal(25_000, source.FrequencyStep.Hertz); // not one of FM's steps: AUD-95's symptom

    await source.StepFrequencyUpAsync();
    Assert.Equal(101_525_000, _receiver.CurrentFrequency);

    await Assert.ThrowsAsync<FrequencyStepNotAllowedException>(() => source.SetFrequencyStepAsync(new Frequency(25_000)));
    await source.SetFrequencyStepAsync(new Frequency(100_000));
    Assert.Equal(100_000, source.FrequencyStep.Hertz);
  }

  // ── start-up restore ─────────────────────────────────────────────────────

  private static IConfigurationManager Store(string band, long frequencyHz, long stepHz)
  {
    Dictionary<string, string> values = new()
    {
      ["Radio:LastBand"] = band,
      ["Radio:LastFrequency"] = frequencyHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ["Radio:LastFrequencyStep"] = stepHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    Mock<IConfigurationStore> store = new();
    store
      .Setup(s => s.GetEntryAsync(It.IsAny<string>(), It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string key, ConfigurationReadMode _, CancellationToken _) =>
        values.TryGetValue(key, out string? v) ? new ConfigurationEntry { Key = key, Value = v } : null);

    Mock<IConfigurationManager> manager = new();
    manager.Setup(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    manager.Setup(m => m.GetStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(store.Object);
    return manager.Object;
  }

  // The restore is private and runs on the source's first play, which needs an audio engine; it is
  // invoked directly here. It is the code path the box runs, not a copy of it.
  private static Task RestoreAsync(SDRRadioAudioSource source) =>
    (Task)typeof(SDRRadioAudioSource)
      .GetMethod("RestorePersistedRadioPreferencesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
      .Invoke(source, new object[] { CancellationToken.None })!;

  private int Warnings() => _logger.Invocations.Count(i =>
    i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning);

  [Fact]
  public async Task Restore_ASavedStepTheBandAllows_IsRestored()
  {
    SDRRadioAudioSource source = CreateSource(Store("FM", 101_500_000, 50_000));

    await RestoreAsync(source);

    Assert.Equal(BandType.FM, _receiver.CurrentBand.Type);
    Assert.Equal(101_500_000, _receiver.CurrentFrequency);
    Assert.Equal(50_000, source.FrequencyStep.Hertz);
    Assert.Equal(0, Warnings());
  }

  [Theory]
  [InlineData("FM", 101_500_000L, 25_000L, 100_000L)]  // WB's step saved after an AUD-95 tune
  [InlineData("SW", 5_905_000L, 10_000L, 5_000L)]      // AM's step on SW
  [InlineData("AM", 1_010_000L, 5_000L, 10_000L)]      // SW's step on AM
  public async Task Restore_ASavedStepTheBandDoesNotAllow_FallsBackToTheBandDefault_AndWarnsOnce(
    string band, long hz, long savedStep, long expectedStep)
  {
    SDRRadioAudioSource source = CreateSource(Store(band, hz, savedStep));

    await RestoreAsync(source);

    Assert.Equal(hz, _receiver.CurrentFrequency);
    Assert.Equal(expectedStep, source.FrequencyStep.Hertz);
    Assert.Equal(1, Warnings());
  }
}
