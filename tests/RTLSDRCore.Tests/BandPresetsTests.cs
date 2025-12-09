using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class BandPresetsTests
{
    [Fact]
    public void AmBroadcast_HasCorrectFrequencyRange()
    {
        var band = BandPresets.AmBroadcast;

        Assert.Equal(530_000, band.MinFrequencyHz);
        Assert.Equal(1_710_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.AM, band.DefaultModulation);
    }

    [Fact]
    public void FmBroadcast_HasCorrectFrequencyRange()
    {
        var band = BandPresets.FmBroadcast;

        Assert.Equal(87_500_000, band.MinFrequencyHz);
        Assert.Equal(108_000_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.WFM, band.DefaultModulation);
    }

    [Fact]
    public void Shortwave_HasCorrectFrequencyRange()
    {
        var band = BandPresets.Shortwave;

        Assert.Equal(1_600_000, band.MinFrequencyHz);
        Assert.Equal(30_000_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.AM, band.DefaultModulation);
    }

    [Fact]
    public void Aircraft_HasCorrectFrequencyRange()
    {
        var band = BandPresets.Aircraft;

        Assert.Equal(108_000_000, band.MinFrequencyHz);
        Assert.Equal(137_000_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.AM, band.DefaultModulation);
    }

    [Fact]
    public void Weather_HasCorrectFrequencyRange()
    {
        var band = BandPresets.Weather;

        Assert.Equal(162_400_000, band.MinFrequencyHz);
        Assert.Equal(162_550_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.NFM, band.DefaultModulation);
    }

    [Fact]
    public void Vhf_HasCorrectFrequencyRange()
    {
        var band = BandPresets.Vhf;

        Assert.Equal(30_000_000, band.MinFrequencyHz);
        Assert.Equal(300_000_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.NFM, band.DefaultModulation);
    }

    [Fact]
    public void AllBands_ContainsAllBandTypes()
    {
        var bands = BandPresets.AllBands;

        Assert.Equal(6, bands.Count);
        Assert.Contains(bands, b => b.Type == BandType.AM);
        Assert.Contains(bands, b => b.Type == BandType.FM);
        Assert.Contains(bands, b => b.Type == BandType.Shortwave);
        Assert.Contains(bands, b => b.Type == BandType.Aircraft);
        Assert.Contains(bands, b => b.Type == BandType.Weather);
        Assert.Contains(bands, b => b.Type == BandType.VHF);
    }

    [Theory]
    [InlineData(BandType.AM)]
    [InlineData(BandType.FM)]
    [InlineData(BandType.Shortwave)]
    [InlineData(BandType.Aircraft)]
    [InlineData(BandType.Weather)]
    [InlineData(BandType.VHF)]
    public void GetBand_ReturnsCorrectBand(BandType bandType)
    {
        var band = BandPresets.GetBand(bandType);

        Assert.Equal(bandType, band.Type);
    }

    [Fact]
    public void GetBand_CustomBand_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => BandPresets.GetBand(BandType.Custom));
    }

    [Fact]
    public void CreateCustomBand_CreatesValidBand()
    {
        var band = BandPresets.CreateCustomBand(
            "Test Band",
            100_000_000,
            200_000_000,
            ModulationType.NFM,
            25_000);

        Assert.Equal("Test Band", band.Name);
        Assert.Equal(100_000_000, band.MinFrequencyHz);
        Assert.Equal(200_000_000, band.MaxFrequencyHz);
        Assert.Equal(ModulationType.NFM, band.DefaultModulation);
        Assert.Equal(25_000, band.DefaultStepHz);
        Assert.Equal(BandType.Custom, band.Type);
    }

    [Theory]
    [InlineData(98_500_000, BandType.FM)]
    [InlineData(850_000, BandType.AM)]
    [InlineData(121_500_000, BandType.Aircraft)]
    [InlineData(162_475_000, BandType.Weather)]
    public void FindBandForFrequency_FindsCorrectBand(long frequencyHz, BandType expectedBand)
    {
        var band = BandPresets.FindBandForFrequency(frequencyHz);

        Assert.NotNull(band);
        Assert.Equal(expectedBand, band.Type);
    }

    [Fact]
    public void FindBandForFrequency_NoMatchingBand_ReturnsNull()
    {
        var band = BandPresets.FindBandForFrequency(500_000_000);

        // This frequency might be in VHF, but let's test a truly out-of-range one
        var outOfRangeBand = BandPresets.FindBandForFrequency(1_500_000_000);
        Assert.Null(outOfRangeBand);
    }
}
