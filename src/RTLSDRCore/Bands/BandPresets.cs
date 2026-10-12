using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore.Bands;

  /// <summary>
  /// Provides preset band configurations for common radio bands
  /// </summary>
  public static class BandPresets
  {
      /// <summary>
      /// AM Broadcast band (530 kHz - 1710 kHz)
      /// </summary>
      public static RadioBand AmBroadcast => new()
      {
          Type = BandType.AM,
          Name = "AM Broadcast",
          MinFrequencyHz = 530_000,
          MaxFrequencyHz = 1_710_000,
          DefaultStepHz = 10_000,
          AllowedStepSizes = new[] { 1_000L, 9_000L, 10_000L },
          DefaultModulation = ModulationType.AM,
          DefaultBandwidthHz = 10_000,
          Description = "AM radio stations"
      };

      /// <summary>
      /// FM Broadcast band (87.5 MHz - 108 MHz)
      /// </summary>
      public static RadioBand FmBroadcast => new()
      {
          Type = BandType.FM,
          Name = "FM Broadcast",
          MinFrequencyHz = 87_500_000,
          MaxFrequencyHz = 108_000_000,
          DefaultStepHz = 100_000,
          AllowedStepSizes = new[] { 50_000L, 100_000L, 200_000L },
          DefaultModulation = ModulationType.WFM,
          DefaultBandwidthHz = 200_000,
          Description = "FM radio stations"
      };

      /// <summary>
      /// Shortwave band (1.711 MHz - 30 MHz)
      /// </summary>
      /// <remarks>
      /// The band starts 1 kHz above <see cref="AmBroadcast"/>'s top channel (1710 kHz), so the two do
      /// not overlap and no whole-kHz frequency between AM and 30 MHz is outside a preset band
      /// (AUD-102). Until AUD-102 it started at 1.6 MHz, and the 1600–1710 kHz overlap went to AM by
      /// list order alone.
      /// </remarks>
      public static RadioBand Shortwave => new()
      {
          Type = BandType.Shortwave,
          Name = "Shortwave",
          MinFrequencyHz = 1_711_000,
          MaxFrequencyHz = 30_000_000,
          DefaultStepHz = 5_000,
          AllowedStepSizes = new[] { 1_000L, 2_500L, 5_000L },
          DefaultModulation = ModulationType.AM,
          DefaultBandwidthHz = 6_000,
          Description = "International broadcasts, amateur radio"
      };

      /// <summary>
      /// Aircraft/Aviation band (108 MHz - 137 MHz)
      /// </summary>
      public static RadioBand Aircraft => new()
      {
          Type = BandType.Aircraft,
          Name = "Aircraft",
          MinFrequencyHz = 108_000_000,
          MaxFrequencyHz = 137_000_000,
          DefaultStepHz = 25_000,
          AllowedStepSizes = new[] { 8_333L, 25_000L },
          DefaultModulation = ModulationType.AM,
          DefaultBandwidthHz = 8_333,
          Description = "Aviation communications"
      };

      /// <summary>
      /// Weather/NOAA band (162.4 MHz - 162.55 MHz)
      /// </summary>
      public static RadioBand Weather => new()
      {
          Type = BandType.Weather,
          Name = "Weather",
          MinFrequencyHz = 162_400_000,
          MaxFrequencyHz = 162_550_000,
          DefaultStepHz = 25_000,
          AllowedStepSizes = new[] { 5_000L, 25_000L },
          DefaultModulation = ModulationType.NFM,
          DefaultBandwidthHz = 12_500,
          Description = "NOAA Weather Radio"
      };

      /// <summary>
      /// VHF band (30 MHz - 300 MHz)
      /// </summary>
      public static RadioBand Vhf => new()
      {
          Type = BandType.VHF,
          Name = "VHF",
          MinFrequencyHz = 30_000_000,
          MaxFrequencyHz = 300_000_000,
          DefaultStepHz = 12_500,
          AllowedStepSizes = new[] { 5_000L, 7_500L, 12_500L, 15_000L, 25_000L },
          DefaultModulation = ModulationType.NFM,
          DefaultBandwidthHz = 12_500,
          Description = "Amateur radio, public safety"
      };

      /// <summary>
      /// Gets all predefined bands
      /// </summary>
      public static IReadOnlyList<RadioBand> AllBands => new[]
      {
          AmBroadcast,
          FmBroadcast,
          Shortwave,
          Aircraft,
          Weather,
          Vhf
      };

      /// <summary>
      /// Gets a band by type
      /// </summary>
      /// <param name="bandType">Type of band</param>
      /// <returns>Radio band configuration</returns>
      public static RadioBand GetBand(BandType bandType)
      {
          return bandType switch
          {
              BandType.AM => AmBroadcast,
              BandType.FM => FmBroadcast,
              BandType.Shortwave => Shortwave,
              BandType.Aircraft => Aircraft,
              BandType.Weather => Weather,
              BandType.VHF => Vhf,
              _ => throw new ArgumentException($"Unknown band type: {bandType}", nameof(bandType))
          };
      }

      /// <summary>
      /// Creates a custom band with specified parameters
      /// </summary>
      /// <param name="name">Band name</param>
      /// <param name="minFrequencyHz">Minimum frequency in Hz</param>
      /// <param name="maxFrequencyHz">Maximum frequency in Hz</param>
      /// <param name="modulation">Default modulation type</param>
      /// <param name="stepHz">Default tuning step in Hz</param>
      /// <returns>Custom radio band</returns>
      public static RadioBand CreateCustomBand(
          string name,
          long minFrequencyHz,
          long maxFrequencyHz,
          ModulationType modulation,
          long stepHz = 10_000)
      {
          return new RadioBand
          {
              Type = BandType.Custom,
              Name = name,
              MinFrequencyHz = minFrequencyHz,
              MaxFrequencyHz = maxFrequencyHz,
              DefaultStepHz = stepHz,
              DefaultModulation = modulation,
              DefaultBandwidthHz = DSP.DemodulatorFactory.GetRecommendedBandwidth(modulation),
              Description = "Custom band"
          };
      }

      /// <summary>
      /// Finds the band that contains a specific frequency
      /// </summary>
      /// <remarks>
      /// The first band in <see cref="AllBands"/> order that contains the frequency (both edges
      /// included). Where bands share frequencies the order decides, and these edges depend on it:
      /// 30 MHz is Shortwave, not VHF; 87.5–108 MHz is FM, not VHF, and 108 MHz is FM, not Aircraft;
      /// 108–137 MHz is Aircraft and 162.4–162.55 MHz is Weather, both inside VHF. AM and Shortwave
      /// do not overlap (AM ends at 1710 kHz, Shortwave starts at 1711 kHz), so the frequencies
      /// strictly between those two, e.g. 1710.5 kHz, are in no band.
      /// </remarks>
      /// <param name="frequencyHz">Frequency in Hz</param>
      /// <returns>The band containing the frequency, or null if none found</returns>
      public static RadioBand? FindBandForFrequency(long frequencyHz)
      {
          return AllBands.FirstOrDefault(b => b.ContainsFrequency(frequencyHz));
      }
  }
